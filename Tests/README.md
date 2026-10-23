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
- EnableIcon / EnableModel / EnableColor, Access = AdminsAndVips, exclusions
- RequireDiscovery Seen and Crafted, ProgressionGate with KeyAliases and ProgressionOverrides
- Cost (not enough, paid, invalid CostItem warns), VIP bypass

Owned pieces and items are built or crafted through the game; other players are simulated with play-as (Alice, Bob, Carol, Nacho-VIP).
Every test also checks the mod logged no errors.

## Not covered (manual or later)

- The Reskin window itself (picking a cell, presets, the color picker UI) and the classic panel: the apply rules are tested through `ReskinApply`, which the window calls.
- How a model or tint looks on the character: screenshots only (R-02, R-01).
- Linked admin/exclusion sources (AdminSource = RenameIt): the suite uses Own.
- Other players seeing model tints: multiplayer, manual.
