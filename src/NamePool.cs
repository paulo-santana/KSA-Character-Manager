using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CharacterManager;

/// First names read from a mod's name list. Each line is "pool name" or just "name" (pool "common"); '#' starts a comment.
/// A character may use only some pools (e.g. common-f and special-f); by default every pool but "veteran", whose names
/// belong to fixed characters. As in KSP, pools whose name starts with "special" are drawn on 1 roll in 20.
internal sealed class NamePool
{
    private const int SpecialChance = 20;

    private readonly string[] _common;
    private readonly string[] _special;
    private readonly string _surname;

    private NamePool(string[] common, string[] special, string surname)
    {
        _common = common;
        _special = special;
        _surname = surname;
    }

    internal int Count => _common.Length + _special.Length;

    private static readonly Dictionary<string, List<(string pool, string name)>> Files = new();

    internal static NamePool? Load(string path, string surname, IReadOnlyCollection<string>? pools)
    {
        if (!File.Exists(path))
        {
            Log.Error($"name list {path} does not exist");
            return null;
        }
        if (!Files.TryGetValue(path, out List<(string pool, string name)>? lines))
            Files[path] = lines = Read(path);
        List<string> common = new(), special = new();
        foreach ((string pool, string name) in lines)
        {
            if (pools is { Count: > 0 } ? !pools.Contains(pool) : pool == "veteran")
                continue;
            (pool.StartsWith("special", StringComparison.Ordinal) ? special : common).Add(name);
        }
        if (common.Count + special.Count == 0)
        {
            Log.Error($"name list {path} has no names{(pools is { Count: > 0 } ? " in " + string.Join(", ", pools) : "")}");
            return null;
        }
        return new NamePool(common.Distinct().ToArray(), special.Distinct().ToArray(), surname);
    }

    private static List<(string pool, string name)> Read(string path)
    {
        List<(string, string)> lines = new();
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            int sp = line.IndexOf(' ');
            string name = sp < 0 ? line : line[(sp + 1)..].Trim();
            if (name.Length > 0)
                lines.Add((sp < 0 ? "common" : line[..sp], name));
        }
        return lines;
    }

    /// A full name that isUsed does not reject, or null when the pool is exhausted.
    internal string? Next(Func<string, bool> isUsed)
    {
        for (int i = 0; i < 200; i++)
        {
            string[] from = _special.Length > 0 && (_common.Length == 0 || Random.Shared.Next(SpecialChance) == 0) ? _special : _common;
            string name = from[Random.Shared.Next(from.Length)];
            string full = _surname.Length > 0 ? $"{name} {_surname}" : name;
            if (!isUsed(full))
                return full;
        }
        return null;
    }
}
