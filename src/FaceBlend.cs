using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using RenderCore;
using RenderCore.Animation;

namespace CharacterManager;

/// KSP's Kerbal face. In KSP an animator blends face clips by three numbers: Expression (-1 panic ... 0 calm ... +1 whee), and two
/// that only pick among the variants of one expression, Variance and SecondaryVariance (each wanders at random between 0 and 1,
/// settling on a new target every second). A character mod declares the tree in CharacterManager.xml, in its expressions block:
///     <CharacterExpressions Id="(the mod's CharacterExpressions)" Modify="Patch">
///         <CharacterManager.FaceBlend>
///             <Bones><Bone Name="..."/>...</Bones>                  the bones the clips drive; only these are touched
///             <Blend Param="Expression">                            each level blends its children by a parameter
///                 <Child Threshold="-1"><Blend Param="Variance">...</Blend></Child>
///                 <Child Threshold="1"><Clip Ref="(a GltfFile)"/></Child>
///             </Blend>
///         </CharacterManager.FaceBlend>
/// Like Unity's 1D blend trees, the two children around the value are mixed, and the clips of all the levels run on one shared
/// normalised time. A second, same-shaped block, CharacterManager.HeadBlend, is KSP's head layer: the bones of the head and eyes,
/// swaying slowly when calm and looking around when excited or afraid, only for a seated Kerbal (KSP's EVA animator has none).
/// What drives it is the player's choice (View > Mods > Expression): off, the sliders there, or KSP's own rules
/// (ExpressionAI).
internal static class FaceBlend
{
    internal const string Tag = AnimationExtensions.Prefix + "FaceBlend";
    internal const string HeadTag = AnimationExtensions.Prefix + "HeadBlend";
    private static readonly string[] Parameters = { "Expression", "Variance", "SecondaryVariance" };
    /// Seconds the face takes to come in or out when driving is switched.
    private const float FadeSeconds = 0.4f;

    // what the window's sliders set (the Manual mode)
    internal static float Expression;
    internal static float Variance = 0.5f, SecondaryVariance = 0.5f;
    /// KSP's behaviour: each Kerbal's two variances wander by themselves. Off: the sliders' values are used.
    internal static bool Wander = true;
    private static readonly ConditionalWeakTable<KittenRenderable, Processor> ByRenderable = new();
    /// Every face built, for the window to show what each is doing.
    internal static readonly List<WeakReference<Processor>> All = new();

    internal static bool TryGet(KittenRenderable r, out Processor processor) => ByRenderable.TryGetValue(r, out processor!);

    internal sealed class Node
    {
        public string? Param, ClipId;
        public List<(float Threshold, Node Child)> Children = new();
    }

    internal sealed class Declared
    {
        public required string[] Bones;
        public required Node Root;
        public bool Female;
    }

    /// CharacterExpressions id -> its face blend, and its head blend
    internal static readonly Dictionary<string, Declared> ByExpressions = new(StringComparer.Ordinal);
    internal static readonly Dictionary<string, Declared> HeadByExpressions = new(StringComparer.Ordinal);

    internal static void Clear()
    {
        ByExpressions.Clear();
        HeadByExpressions.Clear();
    }

    internal static void Read(XElement block)
    {
        if (AnimationExtensions.IdOf(block) is not { } id)
            return;
        if (block.Element(Tag) is { } face && Declare(face, (string?)face.Attribute("Gender") == "Female") is { } declaredFace)
            ByExpressions[id] = declaredFace;
        if (block.Element(HeadTag) is { } head && Declare(head, false) is { } declaredHead)
            HeadByExpressions[id] = declaredHead;
    }

    private static Declared? Declare(XElement layer, bool female)
    {
        string[] bones = layer.Element("Bones")?.Elements("Bone").Select(b => (string?)b.Attribute("Name")).OfType<string>().ToArray() ?? Array.Empty<string>();
        return layer.Element("Blend") is { } root && bones.Length > 0 ? new Declared { Bones = bones, Root = Parse(root), Female = female } : null;
    }

    private static Node Parse(XElement e)
    {
        if (e.Name.LocalName == "Clip")
            return new Node { ClipId = AnimationExtensions.RefOf(e) };
        Node node = new() { Param = (string?)e.Attribute("Param") };
        foreach (XElement child in e.Elements("Child"))
            if (child.Elements().FirstOrDefault() is { } inner)
                node.Children.Add(((float)child.Attribute("Threshold")!, Parse(inner)));
        return node;
    }

    internal sealed class Runtime
    {
        public int Param;
        public AnimationAssetRef? Clip;
        public string? ClipId;
        public (float Threshold, Runtime Child)[] Children = Array.Empty<(float, Runtime)>();
    }

    private static Runtime? Build(Node node, string characterId)
    {
        if (node.ClipId != null)
            return AnimationExtensions.Load(node.ClipId, characterId) is { } clip ? new Runtime { Clip = clip, ClipId = node.ClipId } : null;
        var children = new List<(float, Runtime)>();
        foreach ((float threshold, Node child) in node.Children.OrderBy(c => c.Threshold))
        {
            if (Build(child, characterId) is not { } built)
                return null;
            children.Add((threshold, built));
        }
        return children.Count == 0 ? null : new Runtime { Param = Math.Max(0, Array.IndexOf(Parameters, node.Param)), Children = children.ToArray() };
    }

    /// The clips Unity's 1D blend would play for these parameter values, with their weights (summing to 1).
    private static void Evaluate(Runtime node, ReadOnlySpan<float> values, float weight, List<(Runtime Leaf, float Weight)> into)
    {
        if (weight <= 0f)
            return;
        if (node.Clip != null)
        {
            into.Add((node, weight));
            return;
        }
        var c = node.Children;
        float v = values[node.Param];
        if (v <= c[0].Threshold)
        {
            Evaluate(c[0].Child, values, weight, into);
            return;
        }
        for (int i = 0; i + 1 < c.Length; i++)
        {
            if (v > c[i + 1].Threshold)
                continue;
            float span = c[i + 1].Threshold - c[i].Threshold;
            float f = span > 1e-6f ? (v - c[i].Threshold) / span : 1f;
            Evaluate(c[i].Child, values, weight * (1f - f), into);
            Evaluate(c[i + 1].Child, values, weight * f, into);
            return;
        }
        Evaluate(c[^1].Child, values, weight, into);
    }

    /// A new model, standing or seated: give it the face (and head) its character declares, if any.
    internal static void Attach(KittenRenderable r, CharacterAvatar avatar, CharacterReference? character, string characterId)
    {
        string? expressions = character?.CharacterExpressions?.Ref;
        if (expressions is null || !ByExpressions.TryGetValue(expressions, out Declared? declared) || Build(declared.Root, characterId) is not { } root)
            return;
        Layer? head = null;
        if (HeadByExpressions.TryGetValue(expressions, out Declared? declaredHead) && Build(declaredHead.Root, characterId) is { } headRoot)
            head = new Layer { Root = headRoot, BoneNames = declaredHead.Bones };
        Processor face = new() { CharacterAvatar = avatar, Priority = 1.5f, Face = new Layer { Root = root, BoneNames = declared.Bones }, Head = head, Owner = r };
        face.Brain.Female = declared.Female;
        avatar.Core.CharacterModel.AnimProcessors.Add(face);
        ByRenderable.AddOrUpdate(r, face);
        All.RemoveAll(w => !w.TryGetTarget(out _));
        All.Add(new WeakReference<Processor>(face));
    }

    /// One blend tree playing onto its bones.
    internal sealed class Layer
    {
        internal required Runtime Root;
        internal required string[] BoneNames;
        private int[]? _bones;
        private TransformTRS[]? _acc, _scratch;
        /// Where the blend is in its cycle, 0..1: like Unity's, it advances by dt over the weighted length each frame, so a changing
        /// mix of clips (the wandering variances) keeps it moving smoothly instead of jumping.
        private float _phase, _fade;
        /// What played last frame, for the window.
        internal readonly List<(Runtime Leaf, float Weight)> Active = new();

        internal void Apply(Skeleton skeleton, Span<TransformTRS> localPose, float dt, ReadOnlySpan<float> values, bool shown)
        {
            _fade = Math.Clamp(_fade + (shown ? dt : -dt) / FadeSeconds, 0f, 1f);
            if (_fade <= 0f)
            {
                Active.Clear();
                return;
            }
            _bones ??= BoneNames.Select(n => skeleton.BoneNames?.FindIndex(b => string.Equals(b, n, StringComparison.OrdinalIgnoreCase)) ?? -1).Where(i => i >= 0).ToArray();
            Active.Clear();
            Evaluate(Root, values, 1f, Active);
            if (Active.Count == 0)
                return;
            // the clips of the whole tree share one normalised time, over the weighted length of the clips
            float length = 0f;
            foreach ((Runtime leaf, float weight) in Active)
                length += weight * leaf.Clip!.AnimLength;
            if (length > 1e-4f)
                _phase = (_phase + dt / length) % 1f;
            _acc ??= new TransformTRS[skeleton.WorldTransforms.Length];
            _scratch ??= new TransformTRS[skeleton.WorldTransforms.Length];
            float total = 0f;
            foreach ((Runtime leaf, float weight) in Active)
            {
                AnimationAssetRef clip = leaf.Clip!;
                clip.Sample(_phase * clip.AnimLength, _scratch);
                total += weight;
                float f = weight / total;
                foreach (int b in _bones)
                    _acc[b] = total == weight ? _scratch[b] : TransformTRS.Lerp(_acc[b], _scratch[b], f);
            }
            foreach (int b in _bones)
                localPose[b] = TransformTRS.Lerp(localPose[b], _acc[b], _fade);
        }
    }

    internal sealed class Processor : CatPostAnim
    {
        internal required Layer Face;
        internal Layer? Head;
        internal required KittenRenderable Owner;
        internal readonly ExpressionAI.Brain Brain = new();

        public override void UpdateLocalPose(float4x4 transform, Skeleton skeleton, Span<TransformTRS> localPose, float dt)
        {
            FaceMode mode = Settings.Current.FaceMode;
            ExpressionAI.ByRenderable.TryGetValue(Owner, out ExpressionAI.Inputs? inputs);
            if (inputs != null)
                Brain.Identify(inputs.Kitten);
            Brain.WanderVariance(dt);
            if (mode == FaceMode.Ksp)
                Brain.Update(dt, inputs);
            Span<float> values = mode == FaceMode.Ksp
                ? stackalloc float[] { Brain.Expression, Brain.Variance, Brain.Secondary }
                : stackalloc float[] { Expression, FaceBlend.Wander ? Brain.Variance : Variance, FaceBlend.Wander ? Brain.Secondary : SecondaryVariance };
            // the face is out while the animator would be in its idle state, and while the player has switched it off
            Face.Apply(skeleton, localPose, dt, values, mode == FaceMode.Manual || (mode == FaceMode.Ksp && !Brain.IsIdle));
            // the head layer runs whatever the Kerbal is doing, but only in a seat: the EVA animator has none
            Head?.Apply(skeleton, localPose, dt, values, mode != FaceMode.Off && inputs?.Seated == true);
        }
    }
}

/// View > Mods > Expression: what drives the face, the numbers KSP's face blend takes, and what each Kerbal is doing with them.
internal static class ExpressionWindow
{
    internal static bool Show;

    internal static void Draw()
    {
        if (!Show)
            return;
        if (ImGui.Begin("Expression", ref Show))
        {
            if (FaceBlend.ByExpressions.Count == 0)
                ImGui.TextWrapped("No character has a face blend (a mod declares one in its CharacterManager.xml).");
            else
                Controls();
        }
        ImGui.End();
    }

    private static void Controls()
    {
        Settings s = Settings.Current;
        FaceMode mode = s.FaceMode;
        ImGui.Text("Face");
        if (ImGui.RadioButton("Game's own###faceOff", mode == FaceMode.Off))
            mode = FaceMode.Off;
        ImGui.SameLine();
        if (ImGui.RadioButton("Sliders###faceManual", mode == FaceMode.Manual))
            mode = FaceMode.Manual;
        ImGui.SameLine();
        if (ImGui.RadioButton("Like KSP###faceKsp", mode == FaceMode.Ksp))
            mode = FaceMode.Ksp;
        if (mode != s.FaceMode)
        {
            s.FaceMode = mode;
            Settings.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Game's own: the game's face expressions.\nSliders: KSP's face clips, with the values set here.\nLike KSP: KSP's face clips, driven by what the Kerbal is going through (weightlessness, speed, spin, jetpack),\nand a seated Kerbal's idle clips on KSP's schedule.");
        ImGui.Separator();
        ImGui.BeginDisabled(mode != FaceMode.Ksp);
        float idleSeconds = s.IdleSeconds;
        int idleChance = s.IdleChancePercent;
        bool changed = ImGui.SliderFloat("Idle check every (s)###idleSeconds", ref idleSeconds, 0.5f, 10f);
        changed |= ImGui.SliderInt("Idle chance (%)###idleChance", ref idleChance, 0, 100);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("A seated Kerbal who stays calm checks this often whether to play an idle clip, with this chance.\nKSP: every 10 s, 10 %. Idle clips wait while time warp is on.");
        if (changed)
        {
            s.IdleSeconds = idleSeconds;
            s.IdleChancePercent = idleChance;
            Settings.Save();
        }
        ImGui.EndDisabled();
        ImGui.Separator();
        ImGui.BeginDisabled(mode != FaceMode.Manual);
        ImGui.SliderFloat("Expression###faceExpression", ref FaceBlend.Expression, -1f, 1f);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("KSP's expression: -1 panic, -0.3 sad, 0 calm, 0.3..0.6 happy, 1 excited.");
        ImGui.Checkbox("Variances wander like in KSP###faceWander", ref FaceBlend.Wander);
        ImGui.BeginDisabled(FaceBlend.Wander);
        ImGui.SliderFloat("Variance###faceVariance", ref FaceBlend.Variance, 0f, 1f);
        ImGui.SliderFloat("Secondary variance###faceSecondary", ref FaceBlend.SecondaryVariance, 0f, 1f);
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        FaceBlend.All.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (WeakReference<FaceBlend.Processor> w in FaceBlend.All)
            if (w.TryGetTarget(out FaceBlend.Processor? face))
                Readout(face);
    }

    private static void Readout(FaceBlend.Processor face)
    {
        ExpressionAI.Brain b = face.Brain;
        ExpressionAI.ExpressionInputs(face.Owner, out ExpressionAI.Inputs? i);
        string name = $"{(b.Kitten.Length > 0 ? b.Kitten : "(not seated or on EVA yet)")}{(i?.Seated == true ? " (seat)" : "")}###face{face.GetHashCode()}";
        if (!ImGui.CollapsingHeader(name, ImGuiTreeNodeFlags.DefaultOpen))
            return;
        ImGui.Text($"expression {b.Expression,6:0.00}   whee {b.Whee:0.00}   panic {b.Panic:0.00}   {(b.IsIdle ? "idle" : "")}");
        ImGui.Text($"courage {b.Courage:0.00}  stupidity {b.Stupidity:0.00}{(b.Badass ? "  badass" : "")}");
        if (i != null)
        {
            ImGui.Text($"speed {i.Speed:0.0} m/s (surface {i.SurfaceSpeed:0.0})  climb {i.VerticalSpeed:0.0}  height {i.RadarAltitude:0}");
            ImGui.Text($"g-force {i.Gee:0.00} (smoothed {b.Gee:0.00})  G-LOC {b.GNormalized * 100f:0}%  spin {i.Rates:0.00} rad/s{(i.Jetpack ? "  RCS on" : "")}");
        }
        foreach ((FaceBlend.Runtime leaf, float weight) in face.Face.Active.OrderByDescending(a => a.Weight).Take(5))
            ImGui.Text($"{weight * 100f,5:0}%  {leaf.ClipId ?? ""}");
        if (face.Head is { } head && head.Active.Count > 0)
        {
            ImGui.Text("head:");
            foreach ((FaceBlend.Runtime leaf, float weight) in head.Active.OrderByDescending(a => a.Weight).Take(3))
                ImGui.Text($"{weight * 100f,5:0}%  {leaf.ClipId ?? ""}");
        }
    }
}

[HarmonyPatch(typeof(ProfilerUi), nameof(ProfilerUi.DrawMenuItems))]
internal static class ExpressionMenu
{
    static void Postfix()
    {
        if (!ImGui.BeginMenu("Mods"))
            return;
        ImGui.MenuItem("Expression", "", ref ExpressionWindow.Show);
        ImGui.EndMenu();
    }
}
