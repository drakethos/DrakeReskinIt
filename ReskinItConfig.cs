using BepInEx.Configuration;
using BepInEx.Logging;
using DrakeModsLibs.API;
using DrakeModsLibs.Permissions;
using DrakeModsLibs.Sync;

namespace DrakesReskinIt;

public enum ReskinAccess
{
    /// <summary>Anyone may reskin (exclusions still apply to non-elevated players).</summary>
    Everyone,

    /// <summary>Only elevated players (admins / VIPs, per the admin settings) may reskin.</summary>
    AdminsAndVips,
}

/// <summary>
/// All gameplay entries are server-synced (via DrakeModsLibs) and covered by <see cref="LockSyncedConfig"/>.
/// Admin + exclusion settings live in a Libs permission profile so they can link to RenameIt's.
/// </summary>
internal static class ReskinItConfig
{
    const string General = "01 - General";
    const string Admin = "02 - Admin";
    const string Features = "03 - Features";
    const string Exclusions = "04 - Exclusions";
    const string Cost = "05 - Cost";

    static ConfigEntry<bool>? _lock;
    static ConfigEntry<bool>? _enableIcon;
    static ConfigEntry<bool>? _enableModel;
    static ConfigEntry<bool>? _enableColor;
    static ConfigEntry<ReskinAccess>? _access;
    static ConfigEntry<bool>? _sameTypeOnly;
    static ConfigEntry<DiscoveryRule>? _requireDiscovery;
    static ConfigEntry<bool>? _showUndiscovered;
    static ConfigEntry<bool>? _progressionGate;
    static ConfigEntry<string>? _keyAliases;
    static ConfigEntry<string>? _progressionOverrides;
    static ConfigEntry<bool>? _costEnabled;
    static ConfigEntry<string>? _costItem;
    static ConfigEntry<int>? _costAmount;

    /// <summary>Admin/VIP + exclusions; may link to RenameIt's (see AdminSource / ExclusionSource).</summary>
    public static DrakePermissionProfile Permissions { get; private set; } = null!;

    public static bool LockSyncedConfig => _lock?.Value ?? true;
    public static bool EnableIcon => _enableIcon?.Value ?? true;
    public static bool EnableModel => _enableModel?.Value ?? true;
    public static bool EnableColor => _enableColor?.Value ?? true;
    public static ReskinAccess Access => _access?.Value ?? ReskinAccess.Everyone;
    public static bool SameTypeOnly => _sameTypeOnly?.Value ?? false;
    public static DiscoveryRule RequireDiscovery => _requireDiscovery?.Value ?? DiscoveryRule.Seen;
    public static bool ShowUndiscovered => _showUndiscovered?.Value ?? true;
    public static bool ProgressionGate => _progressionGate?.Value ?? true;
    public static string KeyAliases => _keyAliases?.Value ?? "";
    public static string ProgressionOverrides => _progressionOverrides?.Value ?? "";
    public static bool CostEnabled => _costEnabled?.Value ?? false;
    public static string CostItem => _costItem?.Value?.Trim() ?? "Coins";
    public static int CostAmount => _costAmount?.Value ?? 10;

    public static void Bind(ConfigFile config, ManualLogSource log)
    {
        var sync = CustomizeLibsAPI.CreateConfigSync(ReskinItPlugin.GUID, ReskinItPlugin.ModName, ReskinItPlugin.Version);

        _lock = sync.BindSynced(config, General, General, "LockSyncedConfig", true,
            "When on, the server's values override clients and only server admins can change synced settings. " +
            "Strongly recommended for public servers.");
        sync.AddLockingConfigEntry(_lock);

        Permissions = DrakePermissionProfiles.Create("ReskinIt");
        Permissions.Bind(config, sync, log, Admin, Exclusions,
            linkTargets: new[] { "RenameIt" },
            defaultAdminSource: "RenameIt",
            out var permissionEntries);

        _enableIcon = sync.BindSynced(config, Features, Features, "EnableIcon", true,
            "Allow changing an item's inventory icon. When EnableIcon, EnableModel and EnableColor are all off, the Reskin tab is hidden.");
        _enableModel = sync.BindSynced(config, Features, Features, "EnableModel", true,
            "Allow changing how an equipped item looks on the character (weapons, shields, armor, tools). " +
            "Only models from the same kind of item fit (one-handed for one-handed, helmet for helmet). Other players see it.");
        _enableColor = sync.BindSynced(config, Features, Features, "EnableColor", true,
            "Allow tinting the icon and/or the equipped model. Other players see model tints.");
        _access = sync.BindSynced(config, Features, Features, "Access", ReskinAccess.Everyone,
            "Who may reskin.\n" +
            "Everyone = all players (exclusions still apply to non-elevated players).\n" +
            "AdminsAndVips = only elevated players, as defined by the 02 - Admin settings (or RenameIt's when AdminSource = RenameIt). " +
            "Rename permissions are not affected.");
        _sameTypeOnly = sync.BindSynced(config, Features, Features, "SameTypeOnly", false,
            "Only allow icons from the same kind of item (weapons for weapons, armor for armor, ...).");
        _requireDiscovery = sync.BindSynced(config, Features, Features, "RequireDiscovery", DiscoveryRule.Seen,
            "Which looks a player may use. Admins/VIPs skip this.\n" +
            "Off = any look in the game.\n" +
            "Seen = only items the character has held at least once (the same discovery that unlocks vanilla recipes).\n" +
            "Crafted = craftable items must have been crafted by the character (counted from install onward, plus carried items they crafted); " +
            "items that can't be crafted (trophies, ores, drops) only need to be Seen.");
        _showUndiscovered = sync.BindSynced(config, Features, Features, "ShowUndiscovered", true,
            "On = locked looks show darkened with the reason on hover. Off = locked looks are hidden (no spoilers).");
        _progressionGate = sync.BindSynced(config, Features, Features, "ProgressionGate", true,
            "Lock looks from parts of the game the server hasn't unlocked yet, using world keys (server-side, a client can't fake them). " +
            "Example: iron gear stays locked until The Elder is defeated. Raw materials are tied to the boss that unlocks them; " +
            "crafted items follow their recipe ingredients. Applies on top of RequireDiscovery. Admins/VIPs skip this.");
        _keyAliases = sync.BindSynced(config, Features, Features, "KeyAliases", "",
            "Swap a boss key for your own key, comma separated: defeated_gdking=mod_iron, defeated_bonemass=mod_mountain. " +
            "Run the vanilla 'setkey mod_iron' command when you want that tier open. " +
            "Boss keys: defeated_eikthyr, defeated_gdking (The Elder), defeated_bonemass, defeated_dragon (Moder), " +
            "defeated_goblinking (Yagluth), defeated_queen, defeated_fader.");
        _progressionOverrides = sync.BindSynced(config, Features, Features, "ProgressionOverrides", "",
            "Pin single items to a key, comma separated: SwordIron=mod_iron, MyModdedAxe=defeated_dragon. " +
            "Use =none to never lock an item. Items use prefab names (as in the spawn command). Overrides win over KeyAliases.");

        _costEnabled = sync.BindSynced(config, Cost, Cost, "CostEnabled", false,
            "Charge CostAmount x CostItem each time a reskin is applied. Reset is always free.");
        _costItem = sync.BindSynced(config, Cost, Cost, "CostItem", "Coins",
            "Prefab name of the cost item (e.g. Coins, Resin, Wood).");
        _costAmount = sync.BindSynced(config, Cost, Cost, "CostAmount", 10,
            "How many CostItem each apply costs.", new AcceptableValueRange<int>(1, 9999));

        // lock + permission profile + 10 features + 3 cost
        sync.FinalizeBinding(log, 1 + permissionEntries + 10 + 3, () => LockSyncedConfig);
    }
}
