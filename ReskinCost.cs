namespace DrakesReskinIt;

/// <summary>
/// Single-line apply cost from <see cref="ReskinItConfig"/>. DevCommands <c>nocost</c> skips payment.
/// TODO: fold into a shared DrakeModsLibs cost helper alongside RenameIt's InventoryCost.
/// </summary>
internal static class ReskinCost
{
    static string? _warnedFor;

    /// <summary>True when a cost must be paid for an apply right now.</summary>
    public static bool Applies(Player? player) =>
        ReskinItConfig.CostEnabled && player != null && !player.NoCostCheat() && TryResolve(out _, out _);

    /// <summary>Rich-text cost line for the panel, or empty when free.</summary>
    public static string Describe(Player? player)
    {
        if (!Applies(player) || !TryResolve(out var sharedName, out var displayName))
            return "";
        var need = ReskinItConfig.CostAmount;
        var have = player!.GetInventory()?.CountItems(sharedName) ?? 0;
        var color = have >= need ? "lime" : "red";
        return $"Cost: {need}x {displayName}  <color={color}>(have {have})</color>";
    }

    public static bool TryPay(Player player, out string error)
    {
        error = "";
        if (!Applies(player) || !TryResolve(out var sharedName, out var displayName))
            return true;

        var inv = player.GetInventory();
        var need = ReskinItConfig.CostAmount;
        if (inv == null || inv.CountItems(sharedName) < need)
        {
            error = $"You need {need}x {displayName}";
            return false;
        }

        inv.RemoveItem(sharedName, need, -1, true);
        return true;
    }

    static bool TryResolve(out string sharedName, out string displayName)
    {
        sharedName = displayName = "";
        var prefabName = ReskinItConfig.CostItem;
        var shared = ObjectDB.instance?.GetItemPrefab(prefabName)?.GetComponent<ItemDrop>()?.m_itemData?.m_shared;
        if (shared == null)
        {
            if (ObjectDB.instance != null && _warnedFor != prefabName)
            {
                _warnedFor = prefabName;
                ReskinItPlugin.Log?.LogWarning($"CostItem '{prefabName}' is not an item prefab; reskins are free until fixed.");
            }
            return false;
        }

        sharedName = shared.m_name;
        displayName = Localization.instance?.Localize(shared.m_name) ?? shared.m_name;
        return true;
    }
}
