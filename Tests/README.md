# ReskinIt automated tests

DrakesTestLab suite for ReskinIt. It runs unattended; results go to `<profile>\BepInEx\config\DrakesTestLab\results\latest.html`.

- `suites/DrakesReskinIt/`: the tests (generated, do not edit by hand).
- `gen_reskinit_suite.py`: writes the suite (`python gen_reskinit_suite.py`). The build runs it and copies the suite into the drakeTest profiles; `Tests/` is never packaged.
- `../Testing/ReskinItTestHooks.cs`: the `reskin.*` test hooks the suite calls. They use the mod's real code paths; no reference to DrakesTestLab, and without it installed they are never called.

## Run

```powershell
..\DrakesTestLab\tools\Run-TestLabSuites.ps1 -Suites DrakesReskinIt -Screenshots Always
```

or everything at once: `..\scripts\Test-All.ps1`. In game: F9 > Tests.

## Covered

- Icons, models (same kind only), icon and model colors, reset (free)
- EnableIcon / EnableModel / EnableColor, Access = AdminsAndVips, exclusions by name and category, the allowlist
- RequireDiscovery Seen and Crafted, ShowUndiscovered, SameTypeOnly, ProgressionGate with KeyAliases and ProgressionOverrides
- Cost (not enough, paid, invalid CostItem warns), VIP bypass
- The Reskin window: opening it (Shift + right-click, Reskin tab), the grid (known / locked cells and their reasons, categories, filters, modes), picking and Apply, Reset with its confirm, presets (save, load, delete), the color picker
- What is drawn: the inventory slot's icon and color, the model and tint in the character's hand, helmet and chest swaps
- `DrakesReskinIt-Linked` (needs RenameIt): AdminSource and ExclusionSource = Own / RenameIt / Merge, VIPs from RenameIt, a renamed and reskinned item, the tabs sharing one menu key

Owned pieces and items are built or crafted through the game; other players are simulated with play-as (Alice, Bob, Carol, Nacho-VIP).
Every test also checks the mod logged no errors.

Cross-mod tests (reskinned goods sold in a shop, a reskinned item on a stand and on the ground) live in `DrakeModsLibs/Tests`.

## Not covered (manual or later)

- The classic (non-UI Toolkit) panel.
- Other players seeing model swaps and tints: multiplayer, manual.
