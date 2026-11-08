using System;
using System.Linq;
using HarmonyLib;
using KSA;
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
        GameMembers.Patch(new Harmony(Id));
    }

    /// After the game has loaded every mod's content (AllModsLoad below). Not StarMap's [StarMapAllModsLoaded]: StarMap calls
    /// that with ModLibrary.LoadAll's arguments, and since KSA 2026.11.10.5801 LoadAll takes one while the attribute only
    /// accepts a method without parameters, so it is never called.
    internal static void AllModsLoaded()
    {
        try
        {
            Catalog.Build();
            if (GameMembers.AnimationOk)
                AnimationExtensions.Read(Catalog.Characters.Select(c => c.Reference.Mod).OfType<Mod>().Distinct());
        }
        catch (Exception e)
        {
            Log.Error("setting up the characters failed", e);
        }
    }

    [StarMapAfterGui]
    public void Gui(double dt) => Window.Draw();
}

[HarmonyPatch(typeof(ModLibrary), nameof(ModLibrary.LoadAll))]
internal static class AllModsLoad
{
    static void Postfix() => CharacterManagerMod.AllModsLoaded();
}

internal static class Log
{
    internal static void Info(string msg) => Console.WriteLine($"[{CharacterManagerMod.Id}] {msg}");
    internal static void Error(string msg, Exception? e = null) => Console.WriteLine($"[{CharacterManagerMod.Id}] ERROR {msg}{(e is null ? "" : ": " + e)}");
}
