using System.Collections.Generic;
using System.Linq;
using Brutal.ImGuiApi;
using KSA;

namespace CharacterManager;

/// The player's options, drawn in the game's own two-column style (label | widget), in View > Mods > Characters.
internal static class Options
{
    internal static void Draw()
    {
        if (Catalog.Characters.Count == 0)
        {
            ImGui.TextWrapped("No characters found yet.");
            return;
        }
        Settings s = Settings.Current;
        bool changed = false;

        // the header is drawn here rather than by BeginRegion, so it can carry the tooltip
        bool open = ImGui.CollapsingHeader(RosterRegion, ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAllColumns);
        RosterTooltip();
        if (open)
        {
            ImGuiHelper.BeginRegion(RosterRegion, border: true);
            ImGui.Columns(); // one column: checkbox first, then the mod's name
            foreach (IGrouping<string, CharacterInfo> mod in Catalog.Characters.GroupBy(c => c.ModId))
            {
                bool load = !s.SkippedMods.Contains(mod.Key);
                if (ImGui.Checkbox($"{mod.Key} ({mod.Count()} characters)###load{mod.Key}", ref load))
                {
                    Toggle(s.SkippedMods, mod.Key, !load);
                    changed = true;
                }
                RosterTooltip();
            }
            ImGuiHelper.EndRegion();
        }

        Regenerate.Draw();

        if (changed)
            Settings.Save();
    }

    private const string RosterRegion = "Roster Character Models";

    private static void RosterTooltip()
    {
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Select from which mods to pull characters from. Persists across New Games. \"Regenerate unused recruits\" below will pick random characters from selected mods.");
    }

    private static void Toggle(HashSet<string> set, string id, bool on)
    {
        if (on) set.Add(id); else set.Remove(id);
    }
}
