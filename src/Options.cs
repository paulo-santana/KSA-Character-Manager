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

        if (RcsPuffs.ByAttachment.Count > 0 && ImGuiHelper.BeginRegion("Jetpacks"))
        {
            ImGui.Columns();
            bool atJetpack = s.RcsPuffsAtJetpack;
            if (ImGui.Checkbox("RCS puffs from the character's own jetpack###rcsPuffs", ref atJetpack))
            {
                s.RcsPuffsAtJetpack = atJetpack;
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("For characters whose mod gives their jetpack its own thruster positions. Only where the puffs are drawn\nchanges: the thrust still pushes from the game's positions. Off: the puffs come from the game's jetpack layout.");
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
            ImGui.SetTooltip("Select from which mods to pull characters from. Persists across New Games.\n\"Regenerate unused recruits\" below will pick random characters from selected mods.");
    }

    private static void Toggle(HashSet<string> set, string id, bool on)
    {
        if (on) set.Add(id); else set.Remove(id);
    }
}
