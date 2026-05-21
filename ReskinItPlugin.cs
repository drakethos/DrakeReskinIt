using BepInEx;
using DrakesWorkshopLibs;

namespace DrakesReskinIt;

[BepInPlugin(GUID, ModName, Version)]
[BepInDependency(CustomizeLibsPlugin.GUID, BepInDependency.DependencyFlags.HardDependency)]
public partial class ReskinItPlugin : BaseUnityPlugin
{
    private void Awake() => Logger.LogInfo($"{ModName} {Version} loaded (Phase 1 stub).");
}
