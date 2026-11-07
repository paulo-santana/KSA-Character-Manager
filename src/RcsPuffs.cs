using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace CharacterManager;

/// RCS puff positions for characters that wear their own jetpack. An EVA kitten's thrusters belong to the shared EVA part
/// (KittenBackPackSubPart): each nozzle pushes from ExhaustLocation and draws its puff at FxExhaustLocation, which only the
/// exhaust visuals read (plumes, trails, sparks, the exhaust light), blowing along FxExhaustDirection. A character mod can
/// give its jetpack attachment its own puffs, in the part's frame (metres, feet at the origin, +X forward, +Y right, -Z up):
///     <CharacterAttachment Id="...">
///         ...
///         <CharacterManager.RcsPuff Nozzle="TranslateRight1" X="..." Y="..." Z="..." DX="..." DY="..." DZ="..."/>
/// (DX/DY/DZ, the direction the puff blows, are optional.)
/// They are applied to the EVA kittens wearing that attachment while the option is on, and undone when it is off. The
/// thrust positions stay the game's.
internal static class RcsPuffs
{
    internal const string Tag = AnimationExtensions.Prefix + "RcsPuff";

    /// CharacterAttachment id -> nozzle template id -> puff position and, if given, direction.
    internal static readonly Dictionary<string, Dictionary<string, (float3 At, float3? Dir)>> ByAttachment = new(StringComparer.Ordinal);

    internal static void Read(XDocument doc)
    {
        foreach (XElement attachment in doc.Descendants("CharacterAttachment"))
        {
            Dictionary<string, (float3, float3?)> puffs = new(StringComparer.Ordinal);
            foreach (XElement e in attachment.Elements(Tag))
                if ((string?)e.Attribute("Nozzle") is { } nozzle)
                {
                    float3 at = new((float?)e.Attribute("X") ?? 0f, (float?)e.Attribute("Y") ?? 0f, (float?)e.Attribute("Z") ?? 0f);
                    float3? dir = e.Attribute("DX") is null ? null
                        : new float3((float?)e.Attribute("DX") ?? 0f, (float?)e.Attribute("DY") ?? 0f, (float?)e.Attribute("DZ") ?? 0f);
                    puffs[nozzle] = (at, dir);
                }
            if (puffs.Count > 0 && (string?)attachment.Attribute("Id") is { } id)
            {
                ByAttachment[id] = puffs;
                Log.Info($"{id}: {puffs.Count} RCS puff positions");
            }
        }
    }

    /// The game's puff positions and directions of the nozzles this mod moved, per EVA kitten.
    private static readonly ConditionalWeakTable<KittenEva, Dictionary<RocketNozzle, (float3 At, float3 Dir)>> Moved = new();

    internal static void Update(KittenEva kitten)
    {
        string? attachment = kitten.Character?.CharacterMMU?.Id;
        bool want = Settings.Current.RcsPuffsAtJetpack && attachment != null && ByAttachment.ContainsKey(attachment);
        bool moved = Moved.TryGetValue(kitten, out Dictionary<RocketNozzle, (float3 At, float3 Dir)>? original);
        if (want == moved)
            return;
        if (!want)
        {
            foreach ((RocketNozzle nozzle, (float3 at, float3 dir)) in original!)
            {
                nozzle.FxLocationAsmb = at;
                nozzle.FxExhaustDirectionAsmb = dir;
            }
            Moved.Remove(kitten);
            return;
        }
        Dictionary<string, (float3 At, float3? Dir)> puffs = ByAttachment[attachment!];
        original = new Dictionary<RocketNozzle, (float3, float3)>();
        var nozzles = kitten.Parts.RocketNozzles.ModulesAndStates.GetEnumerator();
        while (nozzles.MoveNext())
        {
            RocketNozzle nozzle = nozzles.Current.Module;
            if (puffs.TryGetValue(nozzle.TemplateId, out (float3 At, float3? Dir) puff))
            {
                original[nozzle] = (nozzle.FxLocationAsmb, nozzle.FxExhaustDirectionAsmb);
                nozzle.FxLocationAsmb = puff.At;
                if (puff.Dir is { } dir)
                    nozzle.FxExhaustDirectionAsmb = dir;
            }
        }
        Moved.Add(kitten, original);
    }
}

[HarmonyPatch(typeof(KittenEva), nameof(KittenEva.UpdateRenderData))]
internal static class RcsPuffsUpdate
{
    private static bool _failed;

    /// Runs every frame inside the game's update: a member a game update changed must not throw there each frame.
    static void Postfix(KittenEva __instance)
    {
        if (_failed)
            return;
        try
        {
            RcsPuffs.Update(__instance);
        }
        catch (Exception e)
        {
            _failed = true;
            Log.Error("RCS puff positions are off (a game update?)", e);
        }
    }
}
