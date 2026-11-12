using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using HarmonyLib;
using KSA;
using KSA.Rendering;
using RenderCore;

namespace CharacterManager;

/// Animations the game has no slot for. A character mod declares them in a CharacterManager.xml next to its mod.toml, written
/// the way the game patches an asset: each block names one of the mod's own elements by Id with Modify="Patch", and lists sit in
/// a container. The clips are GltfFiles of the mod's asset XML:
///     <Assets>
///         <CharacterGroundAnimations Id="(the mod's CharacterGroundAnimations)" Modify="Patch">
///             <CharacterManager.AnimIdleRcs Ref="(a GltfFile)"/>    while the jetpack's RCS is on (also Walk, MoonWalk, MoonRun,
///                                                                   Jump, JumpLand, Tumble: Anim<slot>Rcs)
///             <CharacterManager.AnimRcsOn Ref="..."/>               once, when RCS is turned on standing still
///             <CharacterManager.AnimRcsOff Ref="..."/>              once, when it is turned off standing still
///             <CharacterManager.AnimLadderIdle Ref="..."/>          holding still on a ladder
///             <CharacterManager.AnimLadderDown Ref="..."/>          climbing down (the game plays the climb up both ways)
///             <CharacterManager.AnimIdleActions>                    idle variants while standing
///                 <GltfFile Ref="..."/>
///             </CharacterManager.AnimIdleActions>
///         </CharacterGroundAnimations>
///         <CharacterMMUAnimations Id="..." Modify="Patch">
///             <CharacterManager.MMUArmExtendAnim Ref="..."/>        the jetpack's arms when RCS is turned on (see ArmSampler)
///             <CharacterManager.MMUFloatAnim Ref="..."/>            floating with RCS off, instead of MMUIdleDefaultAnim
///             <CharacterManager.MMUFloatRcsOnAnim Ref="..."/>       once, when RCS is turned on floating (the arms only)
///             <CharacterManager.MMUFloatRcsOffAnim Ref="..."/>      once, when it is turned off floating
///         </CharacterMMUAnimations>
///         <CharacterAttachment Id="..." Modify="Patch">             RCS puffs (see RcsPuffs)
///     </Assets>
/// The game never reads the file (it isn't one of the mod's assets): since KSA 2026.11.10.5801 it drops a whole element that
/// holds a field it doesn't know, so these can't sit in the asset XML.
internal static class AnimationExtensions
{
    internal const string Prefix = "CharacterManager.";
    internal const string FileName = "CharacterManager.xml";
    internal const string IdleActionsTag = Prefix + "AnimIdleActions";
    internal const string LadderIdleTag = Prefix + "AnimLadderIdle";
    internal const string LadderDownTag = Prefix + "AnimLadderDown";
    internal const string RcsOnTag = Prefix + "AnimRcsOn";
    internal const string RcsOffTag = Prefix + "AnimRcsOff";
    internal const string ArmExtendTag = Prefix + "MMUArmExtendAnim";
    internal const string FloatTag = Prefix + "MMUFloatAnim";
    internal const string FloatRcsOnTag = Prefix + "MMUFloatRcsOnAnim";
    internal const string FloatRcsOffTag = Prefix + "MMUFloatRcsOffAnim";
    /// The game's ground slots that have a version while RCS is on, as Anim<slot>Rcs.
    internal static readonly string[] RcsSlots = { "AnimIdle", "AnimWalk", "AnimMoonWalk", "AnimMoonRun", "AnimJump", "AnimJumpLand", "AnimTumble" };
    /// Seconds a character stands idle before an idle action starts, picked anew each time.
    private const float MinWait = 10f, MaxWait = 20f;
    /// Seconds without moving on a ladder before the hold pose plays. The ladder position only changes on physics steps, and
    /// the clip is picked every rendered frame: without this the clips would swap every frame, restarting each time.
    /// The game has the same problem: it freezes its ladder clip on every frame the position did not change, which is most
    /// frames, so its climb hardly plays at all. For a character with ladder extras the freeze is lifted below.
    private const float LadderHoldAfter = 0.25f;

    private static readonly MethodInfo? LoadClip = AccessTools.Method(typeof(KittenRenderable), "LoadGroundAnim");
    private static readonly AccessTools.FieldRef<KittenRenderable, AnimationAssetRef?> LadderAnim =
        AccessTools.FieldRefAccess<KittenRenderable, AnimationAssetRef?>("_ladderAnim");
    private static readonly AccessTools.FieldRef<KittenRenderable, float> LastLadderRailT =
        AccessTools.FieldRefAccess<KittenRenderable, float>("_lastLadderRailT");
    private static readonly AccessTools.FieldRef<KittenRenderable, CharacterAvatar> Avatar =
        AccessTools.FieldRefAccess<KittenRenderable, CharacterAvatar>("_characterAvatar");
    private static readonly AccessTools.FieldRef<KittenRenderable, AnimationPairBlendSampler?> WalkPair =
        AccessTools.FieldRefAccess<KittenRenderable, AnimationPairBlendSampler?>("_walkPairSampler");
    private static readonly AccessTools.FieldRef<KittenRenderable, AnimationPairBlendSampler?> RunPair =
        AccessTools.FieldRefAccess<KittenRenderable, AnimationPairBlendSampler?>("_runPairSampler");
    private static readonly AccessTools.FieldRef<KittenRenderable, AnimationDirectionalBlendSampler> MmuBlend =
        AccessTools.FieldRefAccess<KittenRenderable, AnimationDirectionalBlendSampler>("_blendSampler");
    private static readonly AccessTools.FieldRef<KittenRenderable, AnimationAssetRef?> GroundRun =
        AccessTools.FieldRefAccess<KittenRenderable, AnimationAssetRef?>("_groundRunAnim");
    /// The renderable's clip field of each RCS slot (walk and low-gravity clips are also blended in the pair samplers).
    private static readonly Dictionary<string, AccessTools.FieldRef<KittenRenderable, AnimationAssetRef?>> SlotFields = new()
    {
        ["AnimIdle"] = AccessTools.FieldRefAccess<KittenRenderable, AnimationAssetRef?>("_groundIdleAnim"),
        ["AnimWalk"] = AccessTools.FieldRefAccess<KittenRenderable, AnimationAssetRef?>("_groundWalkAnim"),
        ["AnimMoonWalk"] = AccessTools.FieldRefAccess<KittenRenderable, AnimationAssetRef?>("_moonWalkAnim"),
        ["AnimMoonRun"] = AccessTools.FieldRefAccess<KittenRenderable, AnimationAssetRef?>("_moonRunAnim"),
        ["AnimJump"] = AccessTools.FieldRefAccess<KittenRenderable, AnimationAssetRef?>("_jumpIntroAnim"),
        ["AnimJumpLand"] = AccessTools.FieldRefAccess<KittenRenderable, AnimationAssetRef?>("_jumpLandAnim"),
        ["AnimTumble"] = AccessTools.FieldRefAccess<KittenRenderable, AnimationAssetRef?>("_flailAnim"),
    };

    /// What a CharacterGroundAnimations declares, as GltfFile ids.
    internal sealed class Declared
    {
        public List<string> IdleActions = new();
        public string? LadderIdle, LadderDown, RcsOn, RcsOff;
        /// game slot (AnimIdle...) -> its clip while RCS is on
        public Dictionary<string, string> Rcs = new();

        public bool Any => IdleActions.Count > 0 || LadderIdle != null || LadderDown != null || RcsOn != null || RcsOff != null || Rcs.Count > 0;
    }

    /// The asset a tag points at (Ref; Id as before KSA 2026.11.10.5801).
    internal static string? RefOf(XElement e) => (string?)(e.Attribute("Ref") ?? e.Attribute("Id"));

    /// The asset a block patches: its Id, as in the game's own Modify="Patch" blocks.
    internal static string? IdOf(XElement e) => (string?)(e.Attribute("Id") ?? e.Attribute("Ref"));

    /// CharacterGroundAnimations id -> what it declares.
    internal static readonly Dictionary<string, Declared> ByGroundAnimations = new(StringComparer.Ordinal);

    /// CharacterMMUAnimations id -> its arm extend clip (a GltfFile id).
    internal static readonly Dictionary<string, string> ArmExtendByMmuAnimations = new(StringComparer.Ordinal);

    /// CharacterMMUAnimations id -> its clip for floating with RCS off (a GltfFile id).
    internal static readonly Dictionary<string, string> FloatByMmuAnimations = new(StringComparer.Ordinal);
    /// CharacterMMUAnimations id -> the arms-only RCS toggles while floating (on, off)
    internal static readonly Dictionary<string, (string? On, string? Off)> FloatTogglesByMmuAnimations = new(StringComparer.Ordinal);

    internal static void Read(IEnumerable<Mod> mods)
    {
        ByGroundAnimations.Clear();
        ArmExtendByMmuAnimations.Clear();
        FloatByMmuAnimations.Clear();
        FloatTogglesByMmuAnimations.Clear();
        RcsPuffs.ByAttachment.Clear();
        foreach (Mod mod in mods)
            {
                string path = Path.Combine(mod.DirectoryPath, FileName);
                if (!File.Exists(path))
                    continue;
                try
                {
                    XDocument doc = XDocument.Load(path);
                    RcsPuffs.Read(doc);
                    foreach (XElement ground in doc.Descendants("CharacterGroundAnimations"))
                    {
                        string? Id(string tag) => ground.Element(tag) is { } e ? RefOf(e) : null;
                        Declared d = new()
                        {
                            IdleActions = ground.Element(IdleActionsTag)?.Elements("GltfFile").Select(RefOf).OfType<string>().ToList() ?? new(),
                            LadderIdle = Id(LadderIdleTag),
                            LadderDown = Id(LadderDownTag),
                            RcsOn = Id(RcsOnTag),
                            RcsOff = Id(RcsOffTag),
                        };
                        foreach (string slot in RcsSlots)
                            if (Id(Prefix + slot + "Rcs") is { } rcs)
                                d.Rcs[slot] = rcs;
                        if (d.Any && IdOf(ground) is { } id)
                            ByGroundAnimations[id] = d;
                    }
                    foreach (XElement mmu in doc.Descendants("CharacterMMUAnimations"))
                    {
                        if (IdOf(mmu) is not { } id)
                            continue;
                        if (mmu.Element(ArmExtendTag) is { } arms && RefOf(arms) is { } extend)
                            ArmExtendByMmuAnimations[id] = extend;
                        if (mmu.Element(FloatTag) is { } floating && RefOf(floating) is { } clip)
                            FloatByMmuAnimations[id] = clip;
                        string? on = mmu.Element(FloatRcsOnTag) is { } onTag ? RefOf(onTag) : null;
                        string? off = mmu.Element(FloatRcsOffTag) is { } offTag ? RefOf(offTag) : null;
                        if (on != null || off != null)
                            FloatTogglesByMmuAnimations[id] = (on, off);
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"{mod.Id}/{FileName} could not be read", e);
                }
            }
        foreach ((string id, Declared d) in ByGroundAnimations)
            Log.Info($"{id}: {d.IdleActions.Count} idle actions" + (d.LadderIdle is null ? "" : ", ladder idle")
                     + (d.LadderDown is null ? "" : ", ladder down") + (d.Rcs.Count == 0 ? "" : $", {d.Rcs.Count} RCS clips")
                     + (d.RcsOn is null ? "" : ", RCS on") + (d.RcsOff is null ? "" : ", RCS off"));
        foreach (string id in ArmExtendByMmuAnimations.Keys.Union(FloatByMmuAnimations.Keys).Union(FloatTogglesByMmuAnimations.Keys))
            Log.Info($"{id}: " + string.Join(", ", new[] { ArmExtendByMmuAnimations.ContainsKey(id) ? "jetpack arm extend clip" : null,
                                                            FloatByMmuAnimations.ContainsKey(id) ? "floating clip" : null,
                                                            FloatTogglesByMmuAnimations.ContainsKey(id) ? "floating RCS toggles" : null }.OfType<string>()));
    }

    private sealed class State
    {
        public required List<AnimationAssetRef> IdleActions;
        public AnimationAssetRef? LadderIdle, LadderDown, LadderUp, RcsOn, RcsOff;
        /// slot -> (the game's clip, the clip while RCS is on); and the pair samplers for both
        public Dictionary<string, (AnimationAssetRef? Game, AnimationAssetRef Rcs)> Rcs = new();
        public AnimationPairBlendSampler? GameWalkPair, GameRunPair, RcsWalkPair, RcsRunPair;
        public double Clock, NextAction;
        public AnimationAssetRef? Playing;  // an idle action or an RCS toggle, played once to its end
        public double PlayingEnd;
        public bool PlayingToggle;
        public bool? RcsWas;
        public bool OnLadder, HoldingLadder, ClimbingDown;
        public double LadderStill;
        public ArmSampler? Arms;
        /// floating with RCS off, and the game's MMU idle it stands in for
        public AnimationAssetRef? Float, FloatRcsOn, FloatRcsOff, MmuIdle;
        public bool WasMmu, WasToggle;
    }

    /// The jetpack's arms. The game has one arm clip, MMUArmRetractAnim (deployed -> stowed), and scrubs it back to its start
    /// when RCS comes on and forward to its end when RCS goes off, so coming on it plays the stow backwards. With an extend
    /// clip the arms play that forward instead, and each toggle plays its clip from the start, in step with the body's
    /// toggle clip. The game keeps moving its scrub, which nothing samples any more.
    internal sealed class ArmSampler : IAnimation
    {
        public required IAnimation Retract, Extend;
        public bool Extending;
        public float Time = float.MaxValue;  // at the end of the clip: deployed or stowed

        private IAnimation Clip => Extending ? Extend : Retract;
        public float AnimLength => Clip.AnimLength;
        public void Sample(float time, Span<TransformTRS> output) => Clip.Sample(float.Min(Time, Clip.AnimLength), output);
        public void SampleRotations(float time, Span<TransformTRS> output) => Clip.SampleRotations(float.Min(Time, Clip.AnimLength), output);
    }

    private static readonly ConditionalWeakTable<KittenRenderable, State> States = new();

    private static double Wait() => MinWait + Random.Shared.NextDouble() * (MaxWait - MinWait);

    private static AnimationAssetRef? Load(string? id, string characterId)
    {
        if (id is null)
            return null;
        try
        {
            // the game's loader takes a reference since KSA 2026.11.10.5801, and only reads its resolved Value
            DataReference<Gltf2Reference> clip = new() { Ref = id, Value = ModLibrary.Get<Gltf2Reference>(id) };
            return LoadClip?.Invoke(null, new object?[] { clip }) as AnimationAssetRef;
        }
        catch (Exception e)
        {
            Log.Error($"animation '{id}' of {characterId} could not be loaded", e);
            return null;
        }
    }

    /// The jetpack's arms played by this mod, if the character's MMU animations have an extend clip.
    private static ArmSampler? Arms(KittenRenderable r, CharacterReference? character, string characterId)
    {
        string? mmu = character?.CharacterMMUAnimations?.Ref;
        if (mmu is null || !ArmExtendByMmuAnimations.TryGetValue(mmu, out string? extendId))
            return null;
        CharacterAvatar.Mmu attachment = Avatar(r).Attachments.Mmu;
        if (attachment.MmuMesh is null || attachment.ArmScrub is null || Load(extendId, characterId) is not { } extend)
            return null;
        ArmSampler arms = new() { Retract = Load(character!.CharacterMMUAnimations!.Value!.MMUArmRetractAnim?.Ref, characterId)!, Extend = extend };
        if (arms.Retract is null)
            return null;
        attachment.MmuMesh.SetAnimation(arms, 0f);
        return arms;
    }

    /// The game's walk and run pairs blend each clip with its low-gravity version (KittenRenderable's constructor).
    private static AnimationPairBlendSampler? Pair(AnimationAssetRef? ground, AnimationAssetRef? lowGravity)
        => ground != null && lowGravity != null ? new AnimationPairBlendSampler(ground, lowGravity) : null;

    /// A new standing model: load what its character declares, if anything.
    internal static void Attach(KittenRenderable r, string characterId)
    {
        CharacterReference? character = Catalog.Find(characterId)?.Reference;
        ArmSampler? arms = Arms(r, character, characterId);
        string? ground = character?.CharacterGroundAnimations?.Ref;
        Declared d = ground != null && ByGroundAnimations.TryGetValue(ground, out Declared? declared) ? declared : new Declared();
        string? mmu = character?.CharacterMMUAnimations?.Ref;
        AnimationAssetRef? floating = mmu != null && FloatByMmuAnimations.TryGetValue(mmu, out string? floatId) ? Load(floatId, characterId) : null;
        (string? floatOn, string? floatOff) = mmu != null && FloatTogglesByMmuAnimations.TryGetValue(mmu, out var floatToggles) ? floatToggles : default;
        AnimationAssetRef? floatRcsOn = Load(floatOn, characterId), floatRcsOff = Load(floatOff, characterId);
        if (!d.Any && arms is null && floating is null && floatRcsOn is null && floatRcsOff is null)
            return;
        State s = new()
        {
            IdleActions = d.IdleActions.Select(id => Load(id, characterId)).OfType<AnimationAssetRef>().ToList(),
            LadderIdle = Load(d.LadderIdle, characterId),
            LadderDown = Load(d.LadderDown, characterId),
            LadderUp = LadderAnim(r),
            RcsOn = Load(d.RcsOn, characterId),
            RcsOff = Load(d.RcsOff, characterId),
            NextAction = Wait(),
            Arms = arms,
            Float = floating,
            FloatRcsOn = floatRcsOn,
            FloatRcsOff = floatRcsOff,
            MmuIdle = Avatar(r).Animations.MmuAnimations.MmuIdleDefaultAnim,
        };
        foreach ((string slot, string id) in d.Rcs)
            if (Load(id, characterId) is { } clip)
                s.Rcs[slot] = (SlotFields[slot](r), clip);
        if (s.Rcs.Count > 0)
        {
            AnimationAssetRef? Clip(string slot, bool rcs) => s.Rcs.TryGetValue(slot, out var c) ? (rcs ? c.Rcs : c.Game) : SlotFields[slot](r);
            s.GameWalkPair = WalkPair(r);
            s.GameRunPair = RunPair(r);
            s.RcsWalkPair = Pair(Clip("AnimWalk", true), Clip("AnimMoonWalk", true));
            s.RcsRunPair = Pair(GroundRun(r), Clip("AnimMoonRun", true) ?? Clip("AnimMoonWalk", true));
        }
        States.AddOrUpdate(r, s);
    }

    /// Before the game picks this frame's clip: advance the clock, put the RCS stance's clips where the game looks for its
    /// ground clips and the floating clip where it looks for its MMU idle, start an RCS toggle animation, and on a ladder put the clip that fits where the game looks for its one
    /// ladder clip. The ladder position rises when climbing up (the forward key; its top hands over to the ladder above),
    /// falls when climbing down and stays put when holding still; a jump of a whole rung or more is a hand-over between
    /// ladders and keeps the direction. Between physics steps it stays put too, so the direction is remembered and the hold
    /// pose waits for LadderHoldAfter.
    internal static void BeforeUpdate(KittenRenderable r, float dt, in LocomotionState locomotion, bool rcs)
    {
        if (!States.TryGetValue(r, out State? s))
            return;
        s.Clock += dt;
        if (locomotion.Mode != LocomotionMode.Mmu)
            s.WasMmu = false;
        Stance(r, s, rcs, dt, locomotion);
        Ladder(r, s, dt, locomotion);
    }

    private static void Stance(KittenRenderable r, State s, bool rcs, float dt, in LocomotionState locomotion)
    {
        if (s.Rcs.Count > 0)
        {
            foreach ((string slot, (AnimationAssetRef? game, AnimationAssetRef rcsClip)) in s.Rcs)
                SlotFields[slot](r) = rcs ? rcsClip : game;
            WalkPair(r) = rcs ? s.RcsWalkPair ?? s.GameWalkPair : s.GameWalkPair;
            RunPair(r) = rcs ? s.RcsRunPair ?? s.GameRunPair : s.GameRunPair;
        }
        bool toggled = s.RcsWas is { } was && was != rcs;
        s.RcsWas = rcs;
        if (s.Arms != null)
        {
            s.Arms.Time = toggled ? 0f : s.Arms.Time + dt;
            s.Arms.Extending = rcs;
        }
        bool floating = locomotion.Mode is LocomotionMode.Airborne or LocomotionMode.Mmu;
        // As in KSP, which only accepts the toggle while idle: the animation plays when standing still or floating; walking,
        // the stance just switches. It replaces an idle action in progress. Floating has its own clips, with only the arms
        // moving: the ground ones are standing poses and would pull the legs down.
        AnimationAssetRef? toggle = floating ? (rcs ? s.FloatRcsOn : s.FloatRcsOff) : rcs ? s.RcsOn : s.RcsOff;
        bool standing = locomotion.Mode == LocomotionMode.Grounded && locomotion.GroundSpeed < KittenLocomotionTuning.Current.IdleSpeedThreshold;
        if (toggled && toggle != null && (standing || floating))
        {
            s.Playing = toggle;
            s.PlayingEnd = s.Clock + toggle.AnimLength;
            s.PlayingToggle = true;
        }
        else if (s.Playing != null && s.PlayingToggle && s.Clock >= s.PlayingEnd && floating)
            s.Playing = null;
        // With RCS off the game is in Airborne mode, floating or falling, and plays the MMU idle every frame: the floating clip
        // and the toggle go there. With RCS on (Mmu mode) it blends the MMU clips by thrust and leaves the clip alone without
        // thrust, so AfterMmu plays the toggle there.
        if (s.Float != null || s.FloatRcsOn != null || s.FloatRcsOff != null)
            Avatar(r).Animations.MmuAnimations.MmuIdleDefaultAnim = locomotion.Mode == LocomotionMode.Airborne
                ? (s.PlayingToggle ? s.Playing : null) ?? s.Float ?? s.MmuIdle : s.MmuIdle;
    }

    /// After the game's Mmu-mode clip choice. Thrust plays the game's blend and cuts a toggle short. Without thrust the game
    /// sets no clip (it only does for MMU idle actions), so the toggle plays here, and the MMU idle comes back once after it ends
    /// or when the character comes out of Airborne mode, whose clip (the floating one, or a toggle) would otherwise stay on.
    internal static void AfterMmu(KittenRenderable r)
    {
        if (!States.TryGetValue(r, out State? s))
            return;
        AnimatedRenderable model = Avatar(r).Core.CharacterModel;
        float blend = KittenLocomotionTuning.Current.AnimBlendTime;
        bool entered = !s.WasMmu;
        s.WasMmu = true;
        bool toggle = s.Playing != null && s.PlayingToggle;
        if (MmuBlend(r).BlendSamplePosition.Length() > 0.001f)  // thrust this frame: the game played its blend, as it tests
        {
            if (toggle)
                s.Playing = null;
            return;
        }
        if (toggle)
        {
            model.SetAnimation(s.Playing!, blend);
            s.WasToggle = true;
        }
        else if ((entered || s.WasToggle) && s.MmuIdle != null)
        {
            model.SetAnimation(s.MmuIdle, blend);
            s.WasToggle = false;
        }
    }

    private static void Ladder(KittenRenderable r, State s, float dt, in LocomotionState locomotion)
    {
        s.HoldingLadder = false;
        s.OnLadder = locomotion.Mode == LocomotionMode.Ladder && (s.LadderIdle != null || s.LadderDown != null);
        if (!s.OnLadder)
        {
            LadderAnim(r) = s.LadderUp;
            return;
        }
        float moved = locomotion.LadderRailT - LastLadderRailT(r);
        if (moved == 0f)
            s.LadderStill += dt;
        else
        {
            s.LadderStill = 0;
            if (moved > -1f && moved < 1f)
                s.ClimbingDown = moved < 0f;
        }
        s.HoldingLadder = s.LadderStill >= LadderHoldAfter && s.LadderIdle != null;
        LadderAnim(r) = s.HoldingLadder ? s.LadderIdle : s.ClimbingDown && s.LadderDown != null ? s.LadderDown : s.LadderUp;
    }

    /// After the game's update: on a ladder it froze the clip because the position did not change this frame. The climb keeps
    /// playing between physics steps, and so does the hold pose; only a character still for a while with no hold pose freezes.
    internal static void AfterUpdate(KittenRenderable r)
    {
        if (States.TryGetValue(r, out State? s) && s.OnLadder && (s.HoldingLadder || s.LadderStill < LadderHoldAfter))
            Avatar(r).Core.CharacterModel.FreezeAnimation = false;
    }

    /// Runs where the game picks the grounded clip. Returns false when an idle action or an RCS toggle plays instead of the
    /// game's choice. Idle actions are for the relaxed stance only: with RCS on the hands stay on the controls.
    internal static bool Ground(KittenRenderable r, AnimatedRenderable model, float groundSpeed, bool rcs)
    {
        if (!States.TryGetValue(r, out State? s))
            return true;
        KittenLocomotionTuning tuning = KittenLocomotionTuning.Current;
        if (groundSpeed >= tuning.IdleSpeedThreshold)  // moving: no action, and the idle wait starts over
        {
            s.Playing = null;
            s.NextAction = s.Clock + Wait();
            return true;
        }
        if (s.Playing is null && !rcs && s.IdleActions.Count > 0 && s.Clock >= s.NextAction)
        {
            s.Playing = s.IdleActions[Random.Shared.Next(s.IdleActions.Count)];
            s.PlayingEnd = s.Clock + s.Playing.AnimLength;
            s.PlayingToggle = false;
        }
        if (s.Playing is null)
            return true;
        if (s.Clock >= s.PlayingEnd || (rcs && !s.PlayingToggle))  // done, or RCS came on during an idle action
        {
            s.Playing = null;
            s.NextAction = s.Clock + Wait();
            return true;
        }
        model.SetAnimation(s.Playing, tuning.AnimBlendTime);
        return false;
    }
}

[HarmonyPatch(typeof(KittenRenderable), MethodType.Constructor, new[] { typeof(string), typeof(bool) })]
internal static class AnimationExtensionsAttach
{
    static void Postfix(KittenRenderable __instance, string characterId, bool seated)
    {
        if (!seated && GameMembers.AnimationOk)
            AnimationExtensions.Attach(__instance, characterId);
    }
}

[HarmonyPatch(typeof(KittenRenderable), "UpdateLocomotionAnimationState")]
internal static class AnimationExtensionsUpdate
{
    static void Prefix(KittenRenderable __instance, float simDt, ref LocomotionState locomotionState, ref KittenAnimInputs animInputs)
    {
        if (GameMembers.AnimationOk)
            AnimationExtensions.BeforeUpdate(__instance, simDt, in locomotionState, animInputs.RcsEnabled);
    }

    static void Postfix(KittenRenderable __instance)
    {
        if (GameMembers.AnimationOk)
            AnimationExtensions.AfterUpdate(__instance);
    }
}

[HarmonyPatch(typeof(KittenRenderable), "UpdateMmuBodyAnimationAsset")]
internal static class AnimationExtensionsMmu
{
    static void Postfix(KittenRenderable __instance)
    {
        if (GameMembers.AnimationOk)
            AnimationExtensions.AfterMmu(__instance);
    }
}

[HarmonyPatch(typeof(KittenRenderable), "UpdateGroundedClipSelection")]
internal static class AnimationExtensionsIdle
{
    static bool Prefix(KittenRenderable __instance, AnimatedRenderable model, ref LocomotionState locomotionState, ref KittenAnimInputs animInputs)
        => !GameMembers.AnimationOk || AnimationExtensions.Ground(__instance, model, locomotionState.GroundSpeed, animInputs.RcsEnabled);
}
