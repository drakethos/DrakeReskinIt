using System;
using System.Collections.Generic;
using System.Linq;
using DrakeModsLibs.Display;
using DrakeModsLibs.Progression;

namespace DrakesReskinIt;

public enum DiscoveryRule
{
    /// <summary>Any look in the game.</summary>
    Off,

    /// <summary>Only looks of items you have held at least once (vanilla discovery).</summary>
    Seen,

    /// <summary>Craftable items must have been crafted by you; everything else falls back to Seen.</summary>
    Crafted,
}

/// <summary>
/// Whether a look (catalog entry) is available to a player: server progression key first, then discovery.
/// Elevated players (admins / VIPs) skip both.
/// </summary>
internal static class ReskinUnlocks
{
    static string? _aliasRaw, _overrideRaw;
    static Dictionary<string, string> _aliases = new Dictionary<string, string>();
    static Dictionary<string, string> _overrides = new Dictionary<string, string>();

    /// <summary>Snapshot for one grid refresh, so per-entry checks stay cheap.</summary>
    public sealed class Context
    {
        internal Context(Player? player, bool elevated)
        {
            Player = player;
            Elevated = elevated;
        }

        public Player? Player { get; }
        public bool Elevated { get; }
    }

    public static Context Begin(Player? player)
    {
        if (player != null && ReskinItConfig.RequireDiscovery == DiscoveryRule.Crafted)
            DrakeDiscovery.RecordCraftedFromInventory(player);
        return new Context(player, ReskinItConfig.Permissions.IsElevated(player));
    }

    /// <summary>Why <paramref name="entry"/> is locked for this player, or null when it is available.</summary>
    public static string? LockReason(IconCatalogEntry entry, Context ctx)
    {
        if (ctx.Elevated)
            return null;
        if (ctx.Player == null)
            return "No player";

        if (ReskinItConfig.ProgressionGate)
        {
            RefreshMaps();
            var key = DrakeProgression.RequiredKey(entry.PrefabName, _overrides, _aliases);
            if (key != null && !DrakeProgression.IsKeyUnlocked(key, ctx.Player))
                return DrakeProgression.BossKeys.Contains(key, StringComparer.OrdinalIgnoreCase)
                    ? $"Defeat {DrakeProgression.DescribeKey(key)} first"
                    : $"Locked by the server (world key: {key})";
        }

        switch (ReskinItConfig.RequireDiscovery)
        {
            case DiscoveryRule.Seen:
                return DrakeDiscovery.HasSeen(ctx.Player, entry.NameToken) ? null : "Not discovered yet";
            case DiscoveryRule.Crafted:
                if (DrakeDiscovery.IsCraftable(entry.PrefabName))
                    return DrakeDiscovery.HasCrafted(ctx.Player, entry.PrefabName) ? null : "Craft one first";
                return DrakeDiscovery.HasSeen(ctx.Player, entry.NameToken) ? null : "Not discovered yet";
            default:
                return null;
        }
    }

    static void RefreshMaps()
    {
        var aliasRaw = ReskinItConfig.KeyAliases;
        if (!string.Equals(aliasRaw, _aliasRaw, StringComparison.Ordinal))
        {
            _aliasRaw = aliasRaw;
            _aliases = DrakeProgression.ParsePairs(aliasRaw);
        }
        var overrideRaw = ReskinItConfig.ProgressionOverrides;
        if (!string.Equals(overrideRaw, _overrideRaw, StringComparison.Ordinal))
        {
            _overrideRaw = overrideRaw;
            _overrides = DrakeProgression.ParsePairs(overrideRaw);
        }
    }
}
