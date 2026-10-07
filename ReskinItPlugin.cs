using System;
using BepInEx;
using BepInEx.Logging;
using DrakeModsLibs;
using DrakeModsLibs.API;
using DrakeModsLibs.Display;
using DrakeModsLibs.UI;
using DrakesReskinIt.UI;
using HarmonyLib;
using Jotunn;
using Jotunn.Utils;

namespace DrakesReskinIt;

[BepInPlugin(GUID, ModName, Version)]
[BepInDependency(Main.ModGuid)]
[BepInDependency(CustomizeLibsPlugin.GUID, BepInDependency.DependencyFlags.HardDependency)]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
public partial class ReskinItPlugin : BaseUnityPlugin
{
    public const string TabId = DrakeTabRegistration.ReskinItTabId;

    /// <summary>Below Rename (100) and Paper (101): Reskin is never the default tab.</summary>
    public const int TabPriority = DrakeTabRegistration.DefaultReskinPriority;

    internal static ManualLogSource? Log { get; private set; }

    static readonly LookPickerPanel Picker = new LookPickerPanel();
    readonly Harmony _harmony = new Harmony(GUID);

    void Awake()
    {
        Log = Logger;
        ReskinItConfig.Bind(Config, Logger);
        CustomizeLibsAPI.RegisterEditValidator(CustomizeOperation.ReskinIcon, (item, player) => CanReskin(item, player, ReskinItConfig.EnableIcon));
        CustomizeLibsAPI.RegisterEditValidator(CustomizeOperation.ReskinModel,
            (item, player) => CanReskin(item, player, ReskinItConfig.EnableModel) && ItemLookService.IsEquippable(item));
        CustomizeLibsAPI.RegisterEditValidator(CustomizeOperation.ReskinColor, (item, player) => CanReskin(item, player, ReskinItConfig.EnableColor));
        RegisterTab();
        _harmony.PatchAll();
        Logger.LogInfo($"{ModName} {Version} loaded.");
    }

    void Update() => Picker.Tick();

    void OnDestroy() => _harmony.UnpatchSelf();

    internal static bool IsInLocalInventory(ItemDrop.ItemData? item)
    {
        var inv = Player.m_localPlayer?.GetInventory();
        return item != null && inv != null && inv.ContainsItem(item);
    }

    static void RegisterTab()
    {
        try
        {
            DrakeTabHost.Register(
                id: TabId,
                title: "Reskin",
                priority: TabPriority,
                isAvailable: IsReskinTabAvailable,
                show: ctx => Picker.Open(ctx.Item, () => DrakeTabHost.NotifyFeatureClosed(TabId)),
                hide: Picker.CloseSilent,
                getHintPhrase: () => "reskin item",
                getTitle: () => "Reskin");
        }
        catch (Exception ex)
        {
            Log?.LogError($"DrakeTabHost.Register failed (libs API mismatch?): {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>ReskinIt's rules for every reskin operation (Libs adds tag blocks on top).</summary>
    static bool CanReskin(ItemDrop.ItemData? item, Player? player, bool featureEnabled)
    {
        if (item == null || !featureEnabled)
            return false;
        var permissions = ReskinItConfig.Permissions;
        if (ReskinItConfig.Access == ReskinAccess.AdminsAndVips && !permissions.IsElevated(player))
            return false;
        return !permissions.IsExcluded(item, player);
    }

    static bool IsReskinTabAvailable(ItemDrop.ItemData item) =>
        IsInLocalInventory(item)
        && item.m_shared?.m_icons is { Length: > 0 }
        && (CustomizeLibsAPI.CanReskinIcon(item, Player.m_localPlayer)
            || CustomizeLibsAPI.CanReskinModel(item, Player.m_localPlayer)
            || CustomizeLibsAPI.CanRecolor(item, Player.m_localPlayer));
}
