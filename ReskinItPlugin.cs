using BepInEx;

namespace DrakesReskinIt;

[BepInPlugin(GUID, ModName, Version)]
    [BepInDependency("com.DrakeMods.DrakesCustomizeLibs", BepInDependency.DependencyFlags.HardDependency)]
public class ReskinItPlugin : BaseUnityPlugin
{
    public const string ModName = "DrakesReskinIt";
    public const string Version = "0.1.0";
    public const string GUID = "com.DrakeMods.DrakesReskinIt";
    private void Awake() => Logger.LogInfo($"{ModName} {Version} loaded (Phase 1 stub).");
}
