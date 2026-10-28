using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSA;

namespace CharacterManager;

/// New crew come from two places only: KittenRosterData.Create (a new universe: the fixed Hunter, Banjo and Polaris, then 17
/// random ones) and CreateKitten (5 more whenever no crew member is free). Both name first and pick the character second, so
/// names are fixed up afterwards. Loaded saves keep their crew as they are.
internal static class Roster
{
    internal const int FixedCrew = 3;

    internal static void Rename(KittenRosterData roster, KittenRosterEntryData entry)
    {
        CharacterInfo? c = Catalog.Find(entry.Character);
        if (c?.Names is null)
            return;
        HashSet<string> used = roster.Kittens.Where(k => !ReferenceEquals(k, entry)).Select(k => k.Name).ToHashSet(StringComparer.Ordinal);
        if (c.Names.Next(used.Contains) is { } name)
        {
            entry.Name = name;
            entry.NameHash = KeyHash.Make(name.AsSpan());
        }
    }
}

[HarmonyPatch(typeof(KittenRosterData), "PickRandomCharacterId")]
internal static class PickCharacter
{
    static bool Prefix(ref string __result)
    {
        if (Settings.Current.SkippedMods.Count == 0)
            return true;
        List<string> ids = Catalog.Characters.Where(c => !Settings.Current.SkippedMods.Contains(c.ModId)).Select(c => c.Id).ToList();
        if (ids.Count == 0)
            return true; // everything switched off: the game's own pick, rather than crew with no character
        __result = ids[Random.Shared.Next(ids.Count)];
        return false;
    }
}

[HarmonyPatch(typeof(KittenRosterData), nameof(KittenRosterData.Create))]
internal static class CreateRoster
{
    static void Postfix(KittenRosterData __result)
    {
        // the fixed three keep their names: the game pairs them by name with their EVA vehicles in the world
        foreach (KittenRosterEntryData k in __result.Kittens.Skip(Roster.FixedCrew))
            Roster.Rename(__result, k);
    }
}

[HarmonyPatch(typeof(KittenRosterData), nameof(KittenRosterData.CreateKitten))]
internal static class CreateCrewMember
{
    static void Postfix(KittenRosterData __instance, KittenRosterEntryData __result) => Roster.Rename(__instance, __result);
}
