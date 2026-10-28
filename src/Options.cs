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

        if (ImGuiHelper.BeginRegion("Roster Character Models"))
        {
            foreach (IGrouping<string, CharacterInfo> mod in Catalog.Characters.GroupBy(c => c.ModId))
            {
                bool load = !s.SkippedMods.Contains(mod.Key);
                if (ImGuiHelper.DrawCheckbox($"{mod.Key} ({mod.Count()} characters)", ref load, isChanged: false))
                {
                    Toggle(s.SkippedMods, mod.Key, !load);
                    changed = true;
                }
            }
            ImGui.TextWrapped("New recruits are picked from these. A mod that comes with a name list names its own characters.");
            ImGui.NextColumn();
            ImGui.NextColumn();
            ImGuiHelper.EndRegion();
        }

        Regenerate.Draw();

        if (changed)
            Settings.Save();
    }

    private static void Toggle(HashSet<string> set, string id, bool on)
    {
        if (on) set.Add(id); else set.Remove(id);
    }
}
