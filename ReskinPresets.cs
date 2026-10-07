using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using DrakeModsLibs.Display;
using UnityEngine;

namespace DrakesReskinIt;

/// <summary>A saved look: icon source, model source, and tints (any part may be empty).</summary>
internal sealed class ReskinPreset
{
    public string Name = "";
    public string? IconRef;
    public string? ModelRef;
    public Color? IconTint;
    public Color? ModelTint;
}

/// <summary>
/// Presets are per player and kept on this PC (not synced, not in the character file), so they work on every server
/// and character. File: <c>BepInEx/config/DrakesReskinIt.presets.txt</c>, one tab-separated preset per line.
/// </summary>
internal static class ReskinPresets
{
    static List<ReskinPreset>? _presets;

    static string FilePath => Path.Combine(Paths.ConfigPath, "DrakesReskinIt.presets.txt");

    public static IReadOnlyList<ReskinPreset> All => _presets ??= Load();

    public static ReskinPreset? Find(string name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds or replaces (same name) and saves.</summary>
    public static void Save(ReskinPreset preset)
    {
        var list = (_presets ??= Load());
        list.RemoveAll(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        list.Add(preset);
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        Write(list);
    }

    public static void Delete(string name)
    {
        var list = (_presets ??= Load());
        if (list.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) > 0)
            Write(list);
    }

    static List<ReskinPreset> Load()
    {
        var list = new List<ReskinPreset>();
        try
        {
            if (!File.Exists(FilePath))
                return list;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;
                var f = line.Split('\t');
                list.Add(new ReskinPreset
                {
                    Name = f[0],
                    IconRef = Field(f, 1),
                    ModelRef = Field(f, 2),
                    IconTint = ItemLookService.ParseColor(Field(f, 3)),
                    ModelTint = ItemLookService.ParseColor(Field(f, 4)),
                });
            }
        }
        catch (Exception ex)
        {
            ReskinItPlugin.Log?.LogWarning($"Could not read presets ({FilePath}): {ex.Message}");
        }
        return list;
    }

    static void Write(List<ReskinPreset> list)
    {
        try
        {
            var lines = new List<string> { "# DrakesReskinIt presets: name<TAB>icon<TAB>model<TAB>icon color<TAB>model color" };
            lines.AddRange(list.Select(p => string.Join("\t",
                Clean(p.Name), p.IconRef ?? "", p.ModelRef ?? "",
                ItemLookService.FormatColor(p.IconTint) ?? "", ItemLookService.FormatColor(p.ModelTint) ?? "")));
            File.WriteAllLines(FilePath, lines);
        }
        catch (Exception ex)
        {
            ReskinItPlugin.Log?.LogWarning($"Could not save presets ({FilePath}): {ex.Message}");
        }
    }

    static string? Field(string[] f, int i) => i < f.Length && f[i].Length > 0 ? f[i] : null;
    static string Clean(string s) => s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ').Trim();
}
