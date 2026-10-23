using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DrakeModsLibs.API;
using DrakeModsLibs.Display;
using HarmonyLib;
using UnityEngine;

namespace DrakesReskinIt.Testing;

/// <summary>DrakesTestLab finds methods marked with an attribute named <c>TestHookAttribute</c>. No reference to DrakesTestLab.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class TestHookAttribute : Attribute
{
    public TestHookAttribute(string name) => Name = name;
    public string Name { get; }
}

/// <summary>
/// Test hooks for automated ReskinIt tests: apply / reset through <see cref="ReskinApply"/> (the Reskin window's own rules) and read
/// the result. Item args: prefab (+ index); looks are item prefab names (icon, model); colors are #rrggbb.
/// </summary>
internal static class ReskinItTestHooks
{
    static Player P => Player.m_localPlayer ?? throw new InvalidOperationException("no local player");

    static string S(Dictionary<string, object?> a, string key, string fallback = "") =>
        a.TryGetValue(key, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? fallback : fallback;

    static int I(Dictionary<string, object?> a, string key, int fallback = 0) =>
        a.TryGetValue(key, out var v) && v != null ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : fallback;

    static ItemDrop.ItemData Item(Dictionary<string, object?> a)
    {
        var prefab = S(a, "prefab");
        var list = P.GetInventory().GetAllItems()
            .Where(i => i?.m_shared != null && ((i.m_dropPrefab != null && i.m_dropPrefab.name.Equals(prefab, StringComparison.OrdinalIgnoreCase))
                                                || i.m_shared.m_name.Equals(prefab, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x).ToList();
        var index = I(a, "index");
        if (list.Count <= index)
            throw new InvalidOperationException($"no inventory stack '{prefab}' (index {index})");
        return list[index];
    }

    static IconCatalogEntry? Entry(string prefab)
    {
        if (prefab.Length == 0)
            return null;
        return IconCatalog.GetAll().FirstOrDefault(e => e.PrefabName.Equals(prefab, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"'{prefab}' is not in the look catalog");
    }

    static Color? Tint(string hex)
    {
        if (hex.Length == 0)
            return null;
        if (!ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c))
            throw new InvalidOperationException($"bad color '{hex}'");
        return c;
    }

    /// <summary>May the current player change it? kind = icon | model | color (ReskinIt's access + exclusions + Libs tag rules).</summary>
    [TestHook("reskin.can")]
    static bool Can(Dictionary<string, object?> args)
    {
        var item = Item(args);
        return S(args, "kind", "icon").ToLowerInvariant() switch
        {
            "icon" => CustomizeLibsAPI.CanReskinIcon(item, P),
            "model" => CustomizeLibsAPI.CanReskinModel(item, P),
            "color" or "colour" or "tint" => CustomizeLibsAPI.CanRecolor(item, P),
            var k => throw new InvalidOperationException($"kind must be icon, model or color (got '{k}')")
        };
    }

    /// <summary>Does the Reskin tab show for this item (any of icon / model / color allowed)?</summary>
    [TestHook("reskin.tabAvailable")]
    static bool TabAvailable(Dictionary<string, object?> args) =>
        AccessTools.Method(typeof(ReskinItPlugin), "IsReskinTabAvailable")?.Invoke(null, new object[] { Item(args) }) is true;

    /// <summary>
    /// Apply a look as the Reskin window does: icon / model = item prefab names, iconTint / modelTint = #rrggbb.
    /// Returns "" when applied, otherwise the message the player sees (nothing changes or is paid then).
    /// </summary>
    [TestHook("reskin.apply")]
    static string Apply(Dictionary<string, object?> args)
    {
        var iconTint = S(args, "iconTint");
        var modelTint = S(args, "modelTint");
        return ReskinApply.TryApply(Item(args), P, Entry(S(args, "icon")), Entry(S(args, "model")),
            iconTint.Length > 0, Tint(iconTint), modelTint.Length > 0, Tint(modelTint)) ?? "";
    }

    [TestHook("reskin.reset")]
    static bool Reset(Dictionary<string, object?> args)
    {
        ReskinApply.Reset(Item(args), P);
        return true;
    }

    /// <summary>The icon override's source item (prefab), "" when original.</summary>
    [TestHook("reskin.icon")]
    static string Icon(Dictionary<string, object?> args) => CustomizeLibsAPI.GetIconOverride(Item(args)) ?? "";

    [TestHook("reskin.model")]
    static string Model(Dictionary<string, object?> args) => CustomizeLibsAPI.GetModelOverride(Item(args)) ?? "";

    [TestHook("reskin.iconTint")]
    static string IconTint(Dictionary<string, object?> args) =>
        CustomizeLibsAPI.GetIconTint(Item(args)) is { } c ? "#" + ColorUtility.ToHtmlStringRGB(c) : "";

    [TestHook("reskin.modelTint")]
    static string ModelTint(Dictionary<string, object?> args) =>
        CustomizeLibsAPI.GetModelTint(Item(args)) is { } c ? "#" + ColorUtility.ToHtmlStringRGB(c) : "";

    /// <summary>Why a look is locked for the current player ("" = available): look = item prefab.</summary>
    [TestHook("reskin.lockReason")]
    static string LockReason(Dictionary<string, object?> args) =>
        ReskinUnlocks.LockReason(Entry(S(args, "look")) ?? throw new InvalidOperationException("needs look"), ReskinUnlocks.Begin(P)) ?? "";

    /// <summary>Would this look fit the item as a model (same kind of equippable)?</summary>
    [TestHook("reskin.modelFits")]
    static bool ModelFits(Dictionary<string, object?> args) => ItemLookService.IsModelCompatible(Item(args), Entry(S(args, "look")));

    [TestHook("reskin.elevated")]
    static bool Elevated() => ReskinItConfig.Permissions.IsElevated(P);

    /// <summary>The cost line the window shows ("" when free).</summary>
    [TestHook("reskin.costLine")]
    static string CostLine() => ReskinCost.Describe(P);
}
