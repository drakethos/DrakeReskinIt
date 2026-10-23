using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DrakeModsLibs.API;
using DrakeModsLibs.Display;
using DrakeModsLibs.Progression;
using DrakesReskinIt.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;

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
        var entry = IconCatalog.GetAll().FirstOrDefault(e => e.PrefabName.Equals(prefab, StringComparison.OrdinalIgnoreCase));
        if (entry != null)
            return entry;
        // The catalog lists each icon once: name the item that took this one (another item reusing the same sprite)
        var sprite = ObjectDB.instance?.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_icons?.FirstOrDefault();
        var holder = sprite != null ? IconCatalog.GetAll().FirstOrDefault(e => e.Sprite == sprite) : null;
        string Flags(string name)
        {
            var go = ObjectDB.instance?.GetItemPrefab(name);
            bool jotunn;
            try { jotunn = Jotunn.Managers.ItemManager.Instance.GetItem(name) != null; } catch { jotunn = false; }
            return go == null ? "missing" : $"scene={(go.scene.IsValid() ? go.scene.name : "none")} jotunn={jotunn} index={ObjectDB.instance!.m_items.IndexOf(go)}";
        }
        throw new InvalidOperationException(holder != null
            ? $"'{prefab}' is not in the look catalog: its icon is listed under '{holder.PrefabName}' ({prefab}: {Flags(prefab)}; {holder.PrefabName}: {Flags(holder.PrefabName)})"
            : $"'{prefab}' is not in the look catalog");
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

    // ---------- the Reskin window (UI Toolkit look picker)

    static ToolkitLookPicker Window()
    {
        var panel = AccessTools.Field(typeof(ReskinItPlugin), "Picker")?.GetValue(null)
                    ?? throw new InvalidOperationException("no look picker");
        return AccessTools.Field(panel.GetType(), "_toolkit")?.GetValue(panel) as ToolkitLookPicker
               ?? throw new InvalidOperationException("the new-look Reskin window has not been built (UseToolkitUi off, or never opened)");
    }

    static T F<T>(object o, string name) => (T)AccessTools.Field(o.GetType(), name).GetValue(o)!;

    static System.Collections.IList Cells() => F<System.Collections.IList>(Window(), "_cells");

    static (VisualElement Root, IconCatalogEntry? Entry, string? Lock, bool Shown) CellInfo(object cell)
    {
        var root = F<VisualElement>(cell, "Root");
        var entry = F<IconCatalogEntry?>(cell, "Entry");
        var lockReason = F<string?>(cell, "Lock");
        return (root, entry, lockReason, entry != null && root.resolvedStyle.display != DisplayStyle.None);
    }

    /// <summary>Open the item's Reskin tab directly (fallback; tests normally Shift+right-click and click the Reskin tab).</summary>
    [TestHook("reskin.open")]
    static bool Open(Dictionary<string, object?> args) =>
        DrakeModsLibs.UI.DrakeTabHost.OpenForItem(Item(args), ReskinItPlugin.TabId);

    [TestHook("reskin.windowOpen")]
    static bool WindowOpen()
    {
        var panel = AccessTools.Field(typeof(ReskinItPlugin), "Picker")?.GetValue(null);
        return panel != null && AccessTools.Property(panel.GetType(), "IsOpen")?.GetValue(panel) is true;
    }

    /// <summary>What the grid shows: "visible=N unlocked=M mode=Both filter=Same type".</summary>
    [TestHook("reskin.grid")]
    static string Grid()
    {
        var w = Window();
        return $"visible={F<int>(w, "_visibleCount")} unlocked={F<int>(w, "_unlockedCount")} mode={F<object>(w, "_mode")} filter={F<string>(w, "_filter")}";
    }

    /// <summary>One look in the grid: "unlocked", "locked: reason" (shown dark) or "absent" (not shown).</summary>
    [TestHook("reskin.cell")]
    static string Cell(Dictionary<string, object?> args)
    {
        var look = S(args, "look");
        foreach (var c in Cells())
        {
            var info = CellInfo(c);
            if (info.Shown && info.Entry!.PrefabName.Equals(look, StringComparison.OrdinalIgnoreCase))
                return info.Lock == null ? "unlocked" : "locked: " + info.Lock;
        }

        return "absent";
    }

    /// <summary>Categories of the looks the grid shows, comma separated.</summary>
    [TestHook("reskin.gridCategories")]
    static string GridCategories() =>
        string.Join(",", Cells().Cast<object>().Select(CellInfo).Where(i => i.Shown).Select(i => i.Entry!.Category.ToString()).Distinct().OrderBy(s => s));

    /// <summary>Click a look in the grid (a real click event on its cell; locked cells just show their reason). False when it is not shown.</summary>
    [TestHook("reskin.pick")]
    static bool Pick(Dictionary<string, object?> args)
    {
        var look = S(args, "look");
        foreach (var c in Cells())
        {
            var info = CellInfo(c);
            if (!info.Shown || !info.Entry!.PrefabName.Equals(look, StringComparison.OrdinalIgnoreCase))
                continue;
            using (var click = ClickEvent.GetPooled())
            {
                click.target = info.Root;
                info.Root.SendEvent(click);
            }

            return true;
        }

        return false;
    }

    /// <summary>What is picked but not applied yet: "icon=X model=Y iconTint=#.. modelTint=#..".</summary>
    [TestHook("reskin.pending")]
    static string Pending()
    {
        var w = Window();
        string Hex(object? c) => c is Color col ? "#" + ColorUtility.ToHtmlStringRGB(col) : "";
        return $"icon={F<IconCatalogEntry?>(w, "_pendingIcon")?.PrefabName} model={F<IconCatalogEntry?>(w, "_pendingModel")?.PrefabName} " +
               $"iconTint={(F<bool>(w, "_iconTintDirty") ? Hex(AccessTools.Field(w.GetType(), "_pendingIconTint").GetValue(w)) : "")} " +
               $"modelTint={(F<bool>(w, "_modelTintDirty") ? Hex(AccessTools.Field(w.GetType(), "_pendingModelTint").GetValue(w)) : "")}";
    }

    /// <summary>The window's mode switch: icon, model or both (the segmented control).</summary>
    [TestHook("reskin.setMode")]
    static bool SetMode(Dictionary<string, object?> args)
    {
        var w = Window();
        var mode = AccessTools.Field(w.GetType(), "_mode").FieldType;
        var value = Enum.Parse(mode, S(args, "mode", "Both"), true);
        AccessTools.Method(w.GetType(), "SetMode").Invoke(w, new[] { value });
        return true;
    }

    /// <summary>The window's filter buttons: "All", "Same type" or a category name.</summary>
    [TestHook("reskin.setFilter")]
    static bool SetFilter(Dictionary<string, object?> args)
    {
        AccessTools.Method(typeof(ToolkitLookPicker), "SetFilter").Invoke(Window(), new object[] { S(args, "filter", "All") });
        return true;
    }

    /// <summary>Pick a colour as the colour picker does: model = true for the model colour, hex = #rrggbb.</summary>
    [TestHook("reskin.pickColor")]
    static bool PickColor(Dictionary<string, object?> args)
    {
        var w = Window();
        var model = string.Equals(S(args, "target", "icon"), "model", StringComparison.OrdinalIgnoreCase);
        AccessTools.Method(w.GetType(), "OpenColor").Invoke(w, new object[] { model });
        var picker = F<object>(w, "_colorPicker");
        if (AccessTools.Property(picker.GetType(), "Picked")?.GetValue(picker) is not Action<string> picked)
            picked = AccessTools.Field(picker.GetType(), "Picked")?.GetValue(picker) as Action<string> ?? throw new InvalidOperationException("colour picker has no Picked handler");
        picked(S(args, "hex"));
        return true;
    }

    /// <summary>Load a saved preset by name (the presets dropdown).</summary>
    [TestHook("reskin.loadPreset")]
    static bool LoadPreset(Dictionary<string, object?> args)
    {
        var name = S(args, "preset");
        var list = ReskinPresets.All;
        for (var i = 0; i < list.Count; i++)
        {
            if (!list[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            AccessTools.Method(typeof(ToolkitLookPicker), "LoadPreset").Invoke(Window(), new object[] { i + 1 });
            return true;
        }

        return false;
    }

    [TestHook("reskin.presets")]
    static string Presets() => string.Join(", ", ReskinPresets.All.Select(p => p.Name));

    [TestHook("reskin.deletePresetFile")]
    static bool DeletePresets()
    {
        foreach (var p in ReskinPresets.All.ToList())
            ReskinPresets.Delete(p.Name);
        return true;
    }

    // ---------- what is really drawn

    static readonly string[] HashFields = { "m_currentRightItemHash", "m_currentLeftItemHash", "m_currentHelmetItemHash", "m_currentChestItemHash", "m_currentLegItemHash", "m_currentShoulderItemHash" };

    /// <summary>The model the character really wears in a slot (right, left, helmet, chest, legs, shoulder): its item prefab name.</summary>
    [TestHook("reskin.wornModel")]
    static string WornModel(Dictionary<string, object?> args)
    {
        var vis = P.GetComponent<VisEquipment>() ?? throw new InvalidOperationException("no VisEquipment");
        var slot = S(args, "slot", "right").ToLowerInvariant();
        var field = slot switch
        {
            "right" => "m_currentRightItemHash", "left" => "m_currentLeftItemHash", "helmet" => "m_currentHelmetItemHash",
            "chest" => "m_currentChestItemHash", "legs" => "m_currentLegItemHash", "shoulder" => "m_currentShoulderItemHash",
            _ => throw new InvalidOperationException($"unknown slot '{slot}'")
        };
        var hash = AccessTools.Field(typeof(VisEquipment), field)?.GetValue(vis) is int h ? h : 0;
        if (hash == 0)
            return "";
        var prefab = ObjectDB.instance.GetItemPrefab(hash);
        return prefab != null ? prefab.name : hash.ToString();
    }

    /// <summary>The colour the worn model in a slot is drawn with (renderer _Color), "" when not tinted.</summary>
    [TestHook("reskin.wornTint")]
    static string WornTint(Dictionary<string, object?> args)
    {
        var vis = P.GetComponent<VisEquipment>() ?? throw new InvalidOperationException("no VisEquipment");
        var slot = S(args, "slot", "right").ToLowerInvariant();
        var instance = slot switch
        {
            "right" => "m_rightItemInstance", "left" => "m_leftItemInstance", "helmet" => "m_helmetItemInstance",
            _ => throw new InvalidOperationException("wornTint supports right, left, helmet")
        };
        if (AccessTools.Field(typeof(VisEquipment), instance)?.GetValue(vis) is not GameObject go || go == null)
            return "";
        var block = new MaterialPropertyBlock();
        var id = Shader.PropertyToID("_Color");
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            r.GetPropertyBlock(block);
            if (block.isEmpty)
                continue;
            var c = block.GetColor(id);
            if (c != Color.white && c != default)
                return "#" + ColorUtility.ToHtmlStringRGB(c);
        }

        return "";
    }

    /// <summary>The icon the inventory shows for the stack: "sprite=<name> color=#rrggbb" (opens the inventory to draw it).</summary>
    [TestHook("reskin.iconShown")]
    static string IconShown(Dictionary<string, object?> args)
    {
        var item = Item(args);
        var gui = InventoryGui.instance ?? throw new InvalidOperationException("no inventory window");
        if (!InventoryGui.IsVisible())
            gui.Show(null);
        var grid = gui.m_playerGrid;
        // redraw the grid the way the inventory window does every frame
        var update = AccessTools.Method(typeof(InventoryGrid), "UpdateInventory", new[] { typeof(Inventory), typeof(Player), typeof(ItemDrop.ItemData) });
        if (update != null)
            update.Invoke(grid, new object?[] { P.GetInventory(), P, null });
        else
            AccessTools.Method(typeof(InventoryGrid), "UpdateGui", new[] { typeof(Player), typeof(ItemDrop.ItemData) })?.Invoke(grid, new object?[] { P, null });
        if (AccessTools.Field(typeof(InventoryGrid), "m_elements")?.GetValue(grid) is not System.Collections.IList elements)
            throw new InvalidOperationException("inventory grid elements not found");
        if (elements.Count == 0)
            return "none: the grid has no slots";
        // Field names differ between game builds: take the element's Vector2i (its slot) and its Image named like "icon"
        var type = elements[0]!.GetType();
        const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var posField = type.GetFields(all).FirstOrDefault(f => f.FieldType == typeof(Vector2i));
        var iconField = type.GetFields(all).FirstOrDefault(f => f.FieldType == typeof(UnityEngine.UI.Image) && f.Name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
                        ?? type.GetFields(all).FirstOrDefault(f => f.FieldType == typeof(UnityEngine.UI.Image));
        if (posField == null || iconField == null)
            return "none: slot fields not found (" + string.Join(", ", type.GetFields(all).Select(f => f.Name + ":" + f.FieldType.Name)) + ")";
        foreach (var e in elements)
        {
            if (posField.GetValue(e) is not Vector2i pos || pos.x != item.m_gridPos.x || pos.y != item.m_gridPos.y)
                continue;
            if (iconField.GetValue(e) is not UnityEngine.UI.Image image || image == null)
                return "none: the slot has no icon image";
            return $"sprite={(image.sprite != null ? image.sprite.name : "")} color=#{ColorUtility.ToHtmlStringRGB(image.color)}";
        }

        return $"none: no slot at {item.m_gridPos.x},{item.m_gridPos.y} among {elements.Count} slots";
    }

    /// <summary>
    /// Forget that this character crafted an item (RequireDiscovery Crafted keeps the list in the character file, so a craft in an
    /// earlier test or run still counts): prefab, or "" for all. Returns how many entries are left.
    /// </summary>
    [TestHook("reskin.forgetCrafted")]
    static int ForgetCrafted(Dictionary<string, object?> args)
    {
        P.m_customData.TryGetValue(DrakeDiscovery.CraftedKey, out var raw);
        var prefab = S(args, "prefab");
        var left = (raw ?? "").Split(',').Where(x => x.Length > 0 && prefab.Length > 0 && !x.Equals(prefab, StringComparison.Ordinal)).ToList();
        P.m_customData[DrakeDiscovery.CraftedKey] = string.Join(",", left);
        return left.Count;
    }

    /// <summary>The icon the game itself reports for the stack (ItemData.GetIcon, which other UIs use too).</summary>
    [TestHook("reskin.itemIcon")]
    static string ItemIcon(Dictionary<string, object?> args) => Item(args).GetIcon() is { } s ? s.name : "";

    /// <summary>Does the inventory slot of the stack show the icon of <c>look</c> (an item prefab)?</summary>
    [TestHook("reskin.iconIs")]
    static bool IconIs(Dictionary<string, object?> args) =>
        IconShown(args).StartsWith("sprite=" + PrefabIcon(args) + " ", StringComparison.Ordinal);

    /// <summary>Wear the stack (as clicking it in the inventory does).</summary>
    [TestHook("reskin.equip")]
    static bool Equip(Dictionary<string, object?> args)
    {
        var item = Item(args);
        return P.IsItemEquiped(item) || P.EquipItem(item, true);
    }

    /// <summary>Sprite name of an item prefab's own icon (to compare with iconShown).</summary>
    [TestHook("reskin.prefabIcon")]
    static string PrefabIcon(Dictionary<string, object?> args)
    {
        var go = ObjectDB.instance.GetItemPrefab(S(args, "look")) ?? throw new InvalidOperationException("unknown item");
        var icons = go.GetComponent<ItemDrop>().m_itemData.m_shared.m_icons;
        return icons != null && icons.Length > 0 && icons[0] != null ? icons[0].name : "";
    }

    [TestHook("reskin.elevated")]
    static bool Elevated() => ReskinItConfig.Permissions.IsElevated(P);

    /// <summary>The cost line the window shows ("" when free).</summary>
    [TestHook("reskin.costLine")]
    static string CostLine() => ReskinCost.Describe(P);
}
