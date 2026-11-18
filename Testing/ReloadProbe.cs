#if DEBUG
// SPIKE (2026-11-09): hot-reload probe for BepInEx ScriptEngine. Debug-only, never packaged.
// Logs what survives a reload so we can see what a reload-safe mod must clean up.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using DrakeModsLibs.API;
using DrakeModsLibs.Tags;
using DrakeModsLibs.UI;
using HarmonyLib;
using UnityEngine;

namespace DrakesReskinIt.Testing;

internal static class ReloadProbe
{
    const string Stamp = "B";
    const string CounterKey = "DrakeReloadProbe.ReskinIt.loads";
    static Harmony? _probeHarmony;

    public static void OnAwake(BaseUnityPlugin plugin, ManualLogSource log)
    {
        // inert unless this copy was loaded by ScriptEngine (hot-swap profile only)
        if (!string.IsNullOrEmpty(typeof(ReloadProbe).Assembly.Location))
            return;
        try
        {
            var n = (AppDomain.CurrentDomain.GetData(CounterKey) as int? ?? 0) + 1;
            AppDomain.CurrentDomain.SetData(CounterKey, n);
            var asm = typeof(ReloadProbe).Assembly;
            log.LogInfo($"[ReloadProbe] Awake load#{n} stamp={Stamp} frame={Time.frameCount} asm={asm.GetName().Name} " +
                        $"asm.Location='{asm.Location}' Info.Location='{plugin.Info?.Location}' " +
                        $"inChainloader={Chainloader.PluginInfos.ContainsKey(ReskinItPlugin.GUID)}");

            // Fresh Harmony id per load: a reused id would let the old copy's (deferred) OnDestroy unpatch the new copy.
            _probeHarmony = new Harmony($"{ReskinItPlugin.GUID}.probe.{Guid.NewGuid():N}");
            var target = AccessTools.Method(typeof(CustomizeLibsAPI), nameof(CustomizeLibsAPI.CanReskinIcon));
            _probeHarmony.Patch(target, postfix: new HarmonyMethod(typeof(ReloadProbe), nameof(Postfix)));
            var info = Harmony.GetPatchInfo(target);
            log.LogInfo($"[ReloadProbe] CanReskinIcon postfixes={info?.Postfixes.Count ?? 0} owners=[{string.Join(", ", info?.Postfixes.Select(p => p.owner) ?? Enumerable.Empty<string>())}]");
            log.LogInfo($"[ReloadProbe] Libs ReskinIcon validators={CountValidators(CustomizeOperation.ReskinIcon)} reskinTabs={CountTabs(ReskinItPlugin.TabId)}");
            log.LogInfo($"[ReloadProbe] AllPatchedMethods owned by any ReskinIt copy={CountOwnedPatches()}");
        }
        catch (Exception ex)
        {
            log.LogError($"[ReloadProbe] Awake failed: {ex}");
        }
    }

    public static void OnDestroy(ManualLogSource log)
    {
        try
        {
            log.LogInfo($"[ReloadProbe] OnDestroy stamp={Stamp} frame={Time.frameCount} (unpatching {_probeHarmony?.Id})");
            _probeHarmony?.UnpatchSelf();
        }
        catch (Exception ex)
        {
            log.LogError($"[ReloadProbe] OnDestroy failed: {ex}");
        }
    }

    static void Postfix() { }

    /// <summary>Hot-swap demo: returns this build's stamp, so a test expecting "B" fails on A and passes after the swap.</summary>
    [TestHook("reskin.hotswapStamp")]
    static string HotSwapStamp() => Stamp;

    static int CountValidators(CustomizeOperation op)
    {
        var field = AccessTools.Field(typeof(CustomizationGatekeeper), "Validators");
        if (field?.GetValue(null) is not IDictionary dict) return -1;
        var total = 0;
        foreach (DictionaryEntry e in dict)
            if (Equals(e.Key, op) && e.Value is ICollection list)
                total += list.Count;
        return total;
    }

    static int CountTabs(string idContains)
    {
        var field = AccessTools.Field(typeof(DrakeTabHost), "Registrations");
        if (field?.GetValue(null) is not IEnumerable regs) return -1;
        var n = 0;
        foreach (var r in regs)
        {
            var id = AccessTools.Property(r.GetType(), "Id")?.GetValue(r) as string
                     ?? AccessTools.Field(r.GetType(), "Id")?.GetValue(r) as string;
            if (id != null && id.IndexOf(idContains, StringComparison.OrdinalIgnoreCase) >= 0) n++;
        }
        return n;
    }

    static int CountOwnedPatches()
    {
        var n = 0;
        foreach (var m in Harmony.GetAllPatchedMethods())
        {
            var info = Harmony.GetPatchInfo(m);
            if (info?.Owners.Any(o => o.StartsWith(ReskinItPlugin.GUID, StringComparison.Ordinal)) == true) n++;
        }
        return n;
    }
}
#endif
