using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using KSA;

namespace CharacterManager;

/// The player's choices, for the whole install like the game's own start-up options. Kept in the game profile
/// (Constants.DocumentsFolderPath), so a Borea instance has its own.
internal sealed class Settings
{
    /// Mods whose characters the game must not recruit.
    public HashSet<string> SkippedMods { get; set; } = new(StringComparer.Ordinal);

    internal static Settings Current { get; private set; } = new();

    private static string FilePath => Path.Combine(Constants.DocumentsFolderPath, "CharacterManager.json");

    internal static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e)
        {
            Log.Error($"{FilePath} could not be read; using defaults", e);
        }
    }

    internal static void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Log.Error($"{FilePath} could not be written", e);
        }
    }
}
