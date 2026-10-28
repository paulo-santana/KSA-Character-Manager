using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSA;
using Tomlet;
using Tomlet.Models;

namespace CharacterManager;

/// One character the game registered, with what its mod declared about it (if anything).
internal sealed class CharacterInfo
{
    public required CharacterReference Reference;
    public required string Id;
    public required string ModId;
    public string DisplayName = "";
    public NamePool? Names;
}

/// Every registered character, grouped by the mod that defined it, plus the optional characters.toml of each mod.
internal static class Catalog
{
    internal const string DeclarationFile = "characters.toml";

    private static readonly FieldInfo? AllCharactersField = AccessTools.Field(typeof(ModLibrary), "AllCharacters");

    internal static readonly List<CharacterInfo> Characters = new();

    internal static CharacterInfo? Find(string id) => Characters.FirstOrDefault(c => c.Id == id);

    internal static List<CharacterReference> Registered() => GetList<CharacterReference>(AllCharactersField);

    private static List<T> GetList<T>(FieldInfo? field)
    {
        object? collection = field?.GetValue(null);
        return collection?.GetType().GetMethod("GetList")?.Invoke(collection, null) as List<T> ?? new();
    }

    internal static void Build()
    {
        Characters.Clear();
        foreach (CharacterReference c in Registered())
            Characters.Add(new CharacterInfo { Reference = c, Id = c.Id, ModId = c.Mod?.Id ?? "?", DisplayName = c.Id });

        foreach (Mod mod in Characters.Select(c => c.Reference.Mod).OfType<Mod>().Distinct())
        {
            string path = Path.Combine(mod.DirectoryPath, DeclarationFile);
            if (!File.Exists(path))
                continue;
            try
            {
                ReadDeclaration(mod, path);
            }
            catch (Exception e)
            {
                Log.Error($"{mod.Id}/{DeclarationFile} could not be read", e);
            }
        }
        Log.Info($"{Characters.Count} characters: " + string.Join(", ", Characters.Select(c => $"{c.Id} ({c.ModId}{(c.Names is null ? "" : ", named")})")));
    }

    private static void ReadDeclaration(Mod mod, string path)
    {
        TomlDocument doc = TomlParser.ParseFile(path);
        if (doc.TryGetValue("character", out TomlValue? list) && list is TomlArray characters)
            foreach (TomlValue v in characters)
            {
                if (v is not TomlTable t || !t.ContainsKey("id"))
                    continue;
                // a mod may only describe its own characters, so two mods cannot fight over one
                CharacterInfo? info = Characters.FirstOrDefault(c => c.Id == t.GetString("id") && c.ModId == mod.Id);
                if (info is null)
                {
                    Log.Error($"{mod.Id}/{DeclarationFile}: '{t.GetString("id")}' is not a character of {mod.Id}");
                    continue;
                }
                if (t.ContainsKey("name"))
                    info.DisplayName = t.GetString("name");
                if (t.ContainsKey("names"))
                {
                    List<string>? pools = t.TryGetValue("pools", out TomlValue? p) && p is TomlArray a
                        ? a.OfType<TomlString>().Select(x => x.Value).ToList() : null;
                    info.Names = NamePool.Load(Path.Combine(mod.DirectoryPath, t.GetString("names")),
                        t.ContainsKey("surname") ? t.GetString("surname") : "", pools);
                }
            }
    }
}
