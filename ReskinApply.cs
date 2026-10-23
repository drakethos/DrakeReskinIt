using DrakeModsLibs.API;
using DrakeModsLibs.Display;
using HarmonyLib;
using UnityEngine;

namespace DrakesReskinIt;

/// <summary>
/// The rules for applying and resetting a look, shared by the Reskin window and the automated tests: permission per kind,
/// model fit, progression / discovery locks, then the cost, then the overrides.
/// </summary>
internal static class ReskinApply
{
    static readonly System.Reflection.MethodInfo? SetupEquipmentMethod = AccessTools.Method(typeof(Humanoid), "SetupEquipment");

    /// <summary>Null when applied, otherwise the message the player sees. Nothing changes (or is paid) when it fails.</summary>
    public static string? TryApply(ItemDrop.ItemData item, Player player,
        IconCatalogEntry? icon, IconCatalogEntry? model,
        bool iconTintDirty, Color? iconTint, bool modelTintDirty, Color? modelTint)
    {
        if (icon == null && model == null && !iconTintDirty && !modelTintDirty)
            return "Pick an icon, model or color first";

        var unlocks = ReskinUnlocks.Begin(player);
        if (icon != null)
        {
            if (!CustomizeLibsAPI.CanReskinIcon(item, player))
                return "Icons can't be changed on this item";
            var locked = ReskinUnlocks.LockReason(icon, unlocks);
            if (locked != null)
                return locked;
        }

        if (model != null)
        {
            if (!CustomizeLibsAPI.CanReskinModel(item, player) || !ItemLookService.IsModelCompatible(item, model))
                return "That model doesn't fit this item";
            var locked = ReskinUnlocks.LockReason(model, unlocks);
            if (locked != null)
                return locked;
        }

        if ((iconTintDirty || modelTintDirty) && !CustomizeLibsAPI.CanRecolor(item, player))
            return "Colors are turned off for this item";

        if (!ReskinCost.TryPay(player, out var error))
            return error;

        if (icon != null) CustomizeLibsAPI.SetIconOverride(item, icon.SourceRef);
        if (model != null) CustomizeLibsAPI.SetModelOverride(item, model.SourceRef);
        if (iconTintDirty) CustomizeLibsAPI.SetIconTint(item, iconTint);
        if (modelTintDirty) CustomizeLibsAPI.SetModelTint(item, modelTint);
        RefreshEquipment(item, player);
        return null;
    }

    /// <summary>Back to the original icon, model and colors (each only where this player may change it). Always free.</summary>
    public static void Reset(ItemDrop.ItemData item, Player player)
    {
        if (CustomizeLibsAPI.CanReskinIcon(item, player)) CustomizeLibsAPI.ClearIconOverride(item);
        if (CustomizeLibsAPI.CanReskinModel(item, player)) CustomizeLibsAPI.SetModelOverride(item, null);
        if (CustomizeLibsAPI.CanRecolor(item, player))
        {
            CustomizeLibsAPI.SetIconTint(item, null);
            CustomizeLibsAPI.SetModelTint(item, null);
        }

        RefreshEquipment(item, player);
    }

    /// <summary>Re-run equipment visuals so model swaps and tints show right away (Humanoid.SetupEquipment is protected: reflection).</summary>
    public static void RefreshEquipment(ItemDrop.ItemData item, Player player)
    {
        if (item.m_equipped)
            SetupEquipmentMethod?.Invoke(player, null);
    }
}
