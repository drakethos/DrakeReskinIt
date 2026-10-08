# DrakesReskinIt

## 0.1.0
- Release build. Pinned to DrakeModsLibs 0.10.0.
- Fix: the plugin crashed at startup on DrakeModsLibs 0.10.0 (`DrakeConfirmPanel` constructor), so the Reskin tab never appeared. The reset-confirm panel now calls the 0.10.0 constructor.

## 0.1.0-beta.1

First public beta: icon, model and color reskins with presets.

### Reskin your items
- **Reskin tab** in the shared inventory menu: Shift + right-click an item, pick **Reskin**. Crafting-style picker with search, a **Same type** filter (default), hover names, and a before/after preview.
- **New icon:** use any game item's icon. Shows in the inventory, hotbar, tooltips and pickup messages.
- **New model:** make an equipped weapon, tool, shield or armor piece look like another item of the same kind. Works in every slot, including weapons on your back, and other players see it.
- **Colors:** tint the icon and/or the equipped model with a color picker. Model colors are visible to other players.
- **Presets:** save a look (icon, model, colors) by name and load it onto any item. Stored on your PC, so they work on any character or server.
- **Apply / Reset:** apply everything in one go; Reset restores the original after a confirmation.

### For server admins
- Turn icon, model and color changes on or off separately (`EnableIcon`, `EnableModel`, `EnableColor`).
- Limit reskinning to admins and VIPs (`Access = AdminsAndVips`).
- Admin/VIP list and item exclusions can be your own or shared with DrakesRenameIt (`AdminSource`, `ExclusionSource`: Own / RenameIt / Merge).
- Unlocks: require that a look's item was seen or crafted first (`RequireDiscovery`), and lock looks from tiers the server hasn't reached (`ProgressionGate`, with `KeyAliases` and `ProgressionOverrides` for moderator keys). Locked looks show as silhouettes with the reason, or stay hidden (`ShowUndiscovered`).
- Optional cost per Apply (`CostEnabled`, `CostItem`, `CostAmount`).
- All settings sync from the server.

### Known limitations
- Dropped items and item / armor stands still show the original model.
- Models only swap between items of the same kind.

### Requirements
- DrakeModsLibs 0.9.11+, Jotunn 2.30.2+, BepInExPack Valheim 5.4.2351+. Everyone on a server needs the mod.
- Works alongside DrakesRenameIt: tabs are Rename, Paper, Reskin.
