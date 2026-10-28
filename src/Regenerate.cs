using System.Collections.Generic;
using System.Linq;
using Brutal.ImGuiApi;
using KSA;

namespace CharacterManager;

/// Replaces the unused recruits of the current game with new ones made under the current options. A crew member's name is
/// their identity (seats hold its hash, an EVA kitten is a vehicle with that Id), so only crew that nothing points at are
/// replaced: not seated or on EVA, not KIA, never flown, and not one of the game's fixed three.
internal static class Regenerate
{
    private static readonly string[] FixedNames = { "Hunter", "Banjo", "Polaris" };
    private static bool _armed;

    internal static List<KittenRosterEntryData> Unused(KittenRosterData roster)
        => roster.Kittens.Where(k => !k.AssignedToVehicle && !k.Kia && k.MissionCount == 0 && !FixedNames.Contains(k.Name)).ToList();

    internal static void Draw()
    {
        KittenRosterData? roster = Universe.KittenRoster;
        if (roster is null || !ImGuiHelper.BeginRegion("Current Game"))
            return;
        int n = Unused(roster).Count;
        ImGui.TextWrapped($"{roster.Kittens.Count} crew, {n} unused recruits (never flown, not seated, not on EVA).");
        ImGui.NextColumn();
        ImGui.NextColumn();
        using (new ImGuiDisabledScope(n == 0))
        {
            if (ImGui.Button(_armed ? $"Click again to replace {n} recruits" : "Regenerate unused recruits"))
            {
                if (_armed)
                    Run(roster);
                _armed = !_armed;
            }
        }
        ImGuiHelper.EndRegion();
    }

    private static void Run(KittenRosterData roster)
    {
        List<KittenRosterEntryData> unused = Unused(roster);
        foreach (KittenRosterEntryData k in unused)
            roster.Kittens.Remove(k);
        for (int i = 0; i < unused.Count; i++)
            roster.CreateKitten(); // picks and names through this mod's patches
        Log.Info($"replaced {unused.Count} unused recruits");
    }
}
