using System;
using HarmonyLib;
using StarMap.API;

namespace CharacterManager;

/// Lets the player choose which mods' characters the game recruits, gives each character the kind of name its mod declares,
/// and adds the crew a mod always brings to a new game. The game's own starting three (Hunter, Banjo, Polaris) are left as they
/// are. Works for any character mod: what the game already knows (every registered character and the mod that defined it) is
/// enough; a characters.toml in a mod only adds name lists, display names and fixed crew.
[StarMapMod]
public class CharacterManagerMod
{
    internal const string Id = "CharacterManager";

    [StarMapBeforeMain]
    public void Init()
    {
        Settings.Load();
        new Harmony(Id).PatchAll(typeof(CharacterManagerMod).Assembly);
    }

    [StarMapAllModsLoaded]
    public void AllModsLoaded()
    {
        try
        {
            Catalog.Build();
        }
        catch (Exception e)
        {
            Log.Error("setting up the characters failed", e);
        }
    }

    [StarMapAfterGui]
    public void Gui(double dt) => Window.Draw();
}

internal static class Log
{
    internal static void Info(string msg) => Console.WriteLine($"[{CharacterManagerMod.Id}] {msg}");
    internal static void Error(string msg, Exception? e = null) => Console.WriteLine($"[{CharacterManagerMod.Id}] ERROR {msg}{(e is null ? "" : ": " + e)}");
}
