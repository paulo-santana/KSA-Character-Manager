using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using HarmonyLib;
using KSA;

namespace CharacterManager;

/// Animations the game has no slot for. A character mod declares them in its own asset XML, next to the game's clips:
///     <CharacterGroundAnimations Id="...">
///         <AnimIdle Id="..."/>
///         <CharacterManager.AnimIdleAction Id="(a GltfFile id)"/>    any number: idle variants while standing
///         <AnimLadder Id="..."/>
///         <CharacterManager.AnimLadderIdle Id="..."/>               holding still on a ladder
///         <CharacterManager.AnimLadderDown Id="..."/>               climbing down (the game plays the climb up both ways)
/// The game reads asset XML with a plain XmlSerializer, which skips elements it does not know, so the file still loads
/// without this mod; the prefix marks the lines as this mod's extension.
internal static class AnimationExtensions
{
    internal const string IdleActionTag = "CharacterManager.AnimIdleAction";
    internal const string LadderIdleTag = "CharacterManager.AnimLadderIdle";
    internal const string LadderDownTag = "CharacterManager.AnimLadderDown";
    /// Seconds a character stands idle before an idle action starts, picked anew each time.
    private const float MinWait = 10f, MaxWait = 20f;
    /// Seconds without moving on a ladder before the hold pose plays. The ladder position only changes on physics steps, and
    /// the clip is picked every rendered frame: without this the clips would swap every frame, restarting each time.
    /// The game has the same problem: it freezes its ladder clip on every frame the position did not change, which is most
    /// frames, so its climb hardly plays at all. For a character with ladder extras the freeze is lifted below.
    private const float LadderHoldAfter = 0.25f;

    private static readonly FieldInfo? AssetsField = AccessTools.Field(typeof(Mod), "Assets");
    private static readonly MethodInfo? LoadClip = AccessTools.Method(typeof(KittenRenderable), "LoadGroundAnim");
    private static readonly AccessTools.FieldRef<KittenRenderable, AnimationAssetRef?> LadderAnim =
        AccessTools.FieldRefAccess<KittenRenderable, AnimationAssetRef?>("_ladderAnim");
    private static readonly AccessTools.FieldRef<KittenRenderable, float> LastLadderRailT =
        AccessTools.FieldRefAccess<KittenRenderable, float>("_lastLadderRailT");
    private static readonly AccessTools.FieldRef<KittenRenderable, CharacterAvatar> Avatar =
        AccessTools.FieldRefAccess<KittenRenderable, CharacterAvatar>("_characterAvatar");

    /// What a CharacterGroundAnimations declares, as GltfFile ids.
    internal sealed class Declared
    {
        public List<string> IdleActions = new();
        public string? LadderIdle, LadderDown;
    }

    /// CharacterGroundAnimations id -> what it declares.
    internal static readonly Dictionary<string, Declared> ByGroundAnimations = new(StringComparer.Ordinal);

    internal static void Read(IEnumerable<Mod> mods)
    {
        ByGroundAnimations.Clear();
        foreach (Mod mod in mods)
            foreach (string asset in AssetsField?.GetValue(mod) as string[] ?? Array.Empty<string>())
            {
                string path = Path.Combine(mod.DirectoryPath, asset);
                try
                {
                    foreach (XElement ground in XDocument.Load(path).Descendants("CharacterGroundAnimations"))
                    {
                        Declared d = new()
                        {
                            IdleActions = ground.Elements(IdleActionTag).Select(e => (string?)e.Attribute("Id")).OfType<string>().ToList(),
                            LadderIdle = (string?)ground.Element(LadderIdleTag)?.Attribute("Id"),
                            LadderDown = (string?)ground.Element(LadderDownTag)?.Attribute("Id"),
                        };
                        if ((d.IdleActions.Count > 0 || d.LadderIdle != null || d.LadderDown != null) && (string?)ground.Attribute("Id") is { } id)
                            ByGroundAnimations[id] = d;
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"{mod.Id}/{asset} could not be read for animation extensions", e);
                }
            }
        foreach ((string id, Declared d) in ByGroundAnimations)
            Log.Info($"{id}: {d.IdleActions.Count} idle actions" + (d.LadderIdle is null ? "" : ", ladder idle") + (d.LadderDown is null ? "" : ", ladder down"));
    }

    private sealed class State
    {
        public required List<AnimationAssetRef> IdleActions;
        public AnimationAssetRef? LadderIdle, LadderDown, LadderUp;
        public double Clock, NextAction;
        public AnimationAssetRef? Playing;
        public double PlayingEnd;
        public bool OnLadder, HoldingLadder, ClimbingDown;
        public double LadderStill;
    }

    private static readonly ConditionalWeakTable<KittenRenderable, State> States = new();

    private static double Wait() => MinWait + Random.Shared.NextDouble() * (MaxWait - MinWait);

    private static AnimationAssetRef? Load(string? id, string characterId)
    {
        if (id is null)
            return null;
        try
        {
            return LoadClip?.Invoke(null, new object?[] { ModLibrary.Get<Gltf2Reference>(id) }) as AnimationAssetRef;
        }
        catch (Exception e)
        {
            Log.Error($"animation '{id}' of {characterId} could not be loaded", e);
            return null;
        }
    }

    /// A new standing model: load what its character declares, if anything.
    internal static void Attach(KittenRenderable r, string characterId)
    {
        string? ground = Catalog.Find(characterId)?.Reference.CharacterGroundAnimations?.Id;
        if (ground is null || !ByGroundAnimations.TryGetValue(ground, out Declared? d))
            return;
        States.AddOrUpdate(r, new State
        {
            IdleActions = d.IdleActions.Select(id => Load(id, characterId)).OfType<AnimationAssetRef>().ToList(),
            LadderIdle = Load(d.LadderIdle, characterId),
            LadderDown = Load(d.LadderDown, characterId),
            LadderUp = LadderAnim(r),
            NextAction = Wait(),
        });
    }

    /// Before the game picks this frame's clip: advance the clock, and on a ladder put the clip that fits where the game will
    /// look for its one ladder clip. The ladder position rises when climbing up (the forward key; its top hands over to the
    /// ladder above), falls when climbing down and stays put when holding still; a jump of a whole rung or more is a hand-over
    /// between ladders and keeps the direction. Between physics steps it stays put too, so the direction is remembered and the
    /// hold pose waits for LadderHoldAfter.
    internal static void BeforeUpdate(KittenRenderable r, float dt, in LocomotionState locomotion)
    {
        if (!States.TryGetValue(r, out State? s))
            return;
        s.Clock += dt;
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

    /// Runs where the game picks the grounded clip. Returns false when an idle action plays instead of the game's choice.
    internal static bool Ground(KittenRenderable r, AnimatedRenderable model, float groundSpeed)
    {
        if (!States.TryGetValue(r, out State? s) || s.IdleActions.Count == 0)
            return true;
        KittenLocomotionTuning tuning = KittenLocomotionTuning.Current;
        if (groundSpeed >= tuning.IdleSpeedThreshold)  // moving: no action, and the idle wait starts over
        {
            s.Playing = null;
            s.NextAction = s.Clock + Wait();
            return true;
        }
        if (s.Playing is null && s.Clock >= s.NextAction)
        {
            s.Playing = s.IdleActions[Random.Shared.Next(s.IdleActions.Count)];
            s.PlayingEnd = s.Clock + s.Playing.AnimLength;
        }
        if (s.Playing is null)
            return true;
        if (s.Clock >= s.PlayingEnd)  // done: back to the game's idle
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
        if (!seated)
            AnimationExtensions.Attach(__instance, characterId);
    }
}

[HarmonyPatch(typeof(KittenRenderable), "UpdateLocomotionAnimationState")]
internal static class AnimationExtensionsUpdate
{
    static void Prefix(KittenRenderable __instance, float simDt, ref LocomotionState locomotionState)
        => AnimationExtensions.BeforeUpdate(__instance, simDt, in locomotionState);

    static void Postfix(KittenRenderable __instance) => AnimationExtensions.AfterUpdate(__instance);
}

[HarmonyPatch(typeof(KittenRenderable), "UpdateGroundedClipSelection")]
internal static class AnimationExtensionsIdle
{
    static bool Prefix(KittenRenderable __instance, AnimatedRenderable model, ref LocomotionState locomotionState)
        => AnimationExtensions.Ground(__instance, model, locomotionState.GroundSpeed);
}
