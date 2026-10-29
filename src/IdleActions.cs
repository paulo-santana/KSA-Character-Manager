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

/// Idle variety on the ground. KSA plays a random idle action now and then while a character is seated or floating on the
/// jetpack, but has no such list for standing. A character mod declares one in its own asset XML, next to the game's clips:
///     <CharacterGroundAnimations Id="...">
///         <AnimIdle Id="..."/>
///         <CharacterManager.AnimIdleAction Id="(a GltfFile id)"/>
/// The game reads asset XML with a plain XmlSerializer, which skips elements it does not know, so the file still loads
/// without this mod; the prefix marks the line as this mod's extension.
internal static class IdleActions
{
    internal const string Tag = "CharacterManager.AnimIdleAction";
    /// Seconds a character stands idle before an action starts, picked anew each time.
    private const float MinWait = 10f, MaxWait = 20f;

    private static readonly FieldInfo? AssetsField = AccessTools.Field(typeof(Mod), "Assets");
    private static readonly MethodInfo? LoadClip = AccessTools.Method(typeof(KittenRenderable), "LoadGroundAnim");

    /// CharacterGroundAnimations id -> GltfFile ids of its idle actions.
    internal static readonly Dictionary<string, List<string>> ByGroundAnimations = new(StringComparer.Ordinal);

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
                        List<string> ids = ground.Elements(Tag).Select(e => (string?)e.Attribute("Id")).OfType<string>().ToList();
                        if (ids.Count > 0 && (string?)ground.Attribute("Id") is { } id)
                            ByGroundAnimations[id] = ids;
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"{mod.Id}/{asset} could not be read for idle actions", e);
                }
            }
        foreach ((string id, List<string> clips) in ByGroundAnimations)
            Log.Info($"idle actions of {id}: {string.Join(", ", clips)}");
    }

    private sealed class State
    {
        public required List<AnimationAssetRef> Actions;
        public double Clock, NextAction;
        public AnimationAssetRef? Playing;
        public double PlayingEnd;
    }

    private static readonly ConditionalWeakTable<KittenRenderable, State> States = new();

    private static double Wait() => MinWait + Random.Shared.NextDouble() * (MaxWait - MinWait);

    /// A new standing model: load its character's idle actions, if it has any.
    internal static void Attach(KittenRenderable r, string characterId)
    {
        string? ground = Catalog.Find(characterId)?.Reference.CharacterGroundAnimations?.Id;
        if (ground is null || !ByGroundAnimations.TryGetValue(ground, out List<string>? ids))
            return;
        List<AnimationAssetRef> actions = new();
        foreach (string id in ids)
        {
            try
            {
                Gltf2Reference gltf = ModLibrary.Get<Gltf2Reference>(id);
                if (LoadClip?.Invoke(null, new object?[] { gltf }) is AnimationAssetRef clip)
                    actions.Add(clip);
            }
            catch (Exception e)
            {
                Log.Error($"idle action '{id}' of {characterId} could not be loaded", e);
            }
        }
        if (actions.Count > 0)
            States.AddOrUpdate(r, new State { Actions = actions, NextAction = Wait() });
    }

    internal static void Tick(KittenRenderable r, float dt)
    {
        if (States.TryGetValue(r, out State? s))
            s.Clock += dt;
    }

    /// Runs where the game picks the grounded clip. Returns false when an idle action plays instead of the game's choice.
    internal static bool Ground(KittenRenderable r, AnimatedRenderable model, float groundSpeed)
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
        if (s.Playing is null && s.Clock >= s.NextAction)
        {
            s.Playing = s.Actions[Random.Shared.Next(s.Actions.Count)];
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
internal static class IdleActionsAttach
{
    static void Postfix(KittenRenderable __instance, string characterId, bool seated)
    {
        if (!seated)
            IdleActions.Attach(__instance, characterId);
    }
}

[HarmonyPatch(typeof(KittenRenderable), "UpdateLocomotionAnimationState")]
internal static class IdleActionsClock
{
    static void Prefix(KittenRenderable __instance, float simDt) => IdleActions.Tick(__instance, simDt);
}

[HarmonyPatch(typeof(KittenRenderable), "UpdateGroundedClipSelection")]
internal static class IdleActionsGround
{
    static bool Prefix(KittenRenderable __instance, AnimatedRenderable model, ref LocomotionState locomotionState)
        => IdleActions.Ground(__instance, model, locomotionState.GroundSpeed);
}
