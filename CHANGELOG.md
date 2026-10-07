# DrakesReskinIt

## 0.1.0-beta.1
- First public beta. Please report bugs at https://github.com/drakethos/DrakeReskinIt/issues.
- Aligned with DrakeWorkshop build standards (shared build/, CI framework props, posix Thunderstore zip, DrakeModsLibs 0.9.10, Jotunn 2.30.2, BepInExPack 5.4.2351).
- **Reskin** tab in the DrakeModsLibs inventory menu (Shift + right-click): build-menu style icon picker with item preview, category tabs (Same type default), search, hover names, Apply / Reset.
- Server-synced config: admin/VIP settings and exclusions (each can link to RenameIt's: `AdminSource`, `ExclusionSource` = Own / RenameIt / Merge), `EnableIcon`, `Access` (Everyone / AdminsAndVips), `SameTypeOnly`, optional apply cost. See README.
- **Model changer**: Icon / Model / Both modes; equipped models swap within the same slot type and sync to other players.
- **Recolor**: Color buttons (Jotunn color picker) tint the icon and the equipped model; model tints sync to other players.
- **Presets**: save / load / delete named looks, stored on this PC.
- New settings `EnableModel`, `EnableColor` (22 synced entries).
- Unlocks: `RequireDiscovery` (Off / Seen / Crafted), `ShowUndiscovered`, and a server-side `ProgressionGate` with `KeyAliases` / `ProgressionOverrides` for moderator keys. Locked looks show as silhouettes with the reason.
- Reset asks for confirmation. Reskin tab ranks below Rename and Paper.
