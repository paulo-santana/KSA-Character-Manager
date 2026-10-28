using Brutal.ImGuiApi;
using HarmonyLib;
using KSA;

namespace CharacterManager;

/// View > Mods > Characters.
internal static class Window
{
    internal static bool Show;

    internal static void Draw()
    {
        if (!Show)
            return;
        if (ImGui.Begin("Characters", ref Show))
            Options.Draw();
        ImGui.End();
    }
}

// ProfilerUi.DrawMenuItems is only called inside the game's View menu; a submenu with the same label is shared, so every mod
// can add its own entry
[HarmonyPatch(typeof(ProfilerUi), nameof(ProfilerUi.DrawMenuItems))]
internal static class ViewMenu
{
    static void Postfix()
    {
        if (!ImGui.BeginMenu("Mods"))
            return;
        ImGui.MenuItem("Characters", "", ref Window.Show);
        ImGui.EndMenu();
    }
}
