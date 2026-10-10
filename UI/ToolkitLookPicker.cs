using System;
using System.Collections.Generic;
using System.Linq;
using DrakeModsLibs.API;
using DrakeModsLibs.Display;
using DrakeModsLibs.UI;
using DrakeModsLibs.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace DrakesReskinIt.UI;

/// <summary>
/// The look picker in the suite's new look (a <see cref="UkWindow"/>): the same behaviour as <see cref="ClassicLookPickerPanel"/>,
/// laid out as two columns. Left: mode (Icon / Model / Both), before and after for each part with a colour button (a real colour
/// picker), presets, cost. Right: category chips, search, and the grid. Nothing is written to the item until Apply; one Apply
/// charges the cost once. Confirm and name-prompt popups are the shared Libs ones, which draw in the same look.
/// </summary>
internal sealed class ToolkitLookPicker
{
    enum PickMode { Icon, Model, Both }

    const string FilterAll = "All";
    const string FilterSameType = "Same type";
    const float CellSize = 52f;

    static readonly Color SlotColor = new(0.17f, 0.13f, 0.10f, 0.95f);
    static readonly Color SlotHoverColor = new(0.27f, 0.21f, 0.16f, 0.95f);
    static readonly Color LockedSlotColor = new(0.10f, 0.08f, 0.06f, 0.95f);
    static readonly Color LockedIconColor = new(0f, 0f, 0f, 0.85f); // silhouette, like vanilla unknown recipes

    static readonly string[] Filters =
        new[] { FilterAll, FilterSameType }.Concat(Enum.GetNames(typeof(IconCategory))).ToArray();

    static readonly System.Reflection.MethodInfo? SetupEquipmentMethod =
        HarmonyLib.AccessTools.Method(typeof(Humanoid), "SetupEquipment");

    static readonly DrakeConfirmPanel ResetConfirm = new DrakeConfirmPanel("drakes_reskinit_reset_confirm", null);
    static readonly DrakeTextPromptPanel PresetPrompt = new DrakeTextPromptPanel("drakes_reskinit_preset_prompt");

    readonly UkWindow _window;
    readonly UkSegmented _modeSegmented;
    readonly VisualElement _bigIcon;
    readonly VisualElement _iconWas;
    readonly VisualElement _iconNow;
    readonly VisualElement _modelWas;
    readonly VisualElement _modelNow;
    readonly VisualElement _modelRow;
    readonly Button _iconColorButton;
    readonly Button _modelColorButton;
    readonly VisualElement _iconColorDot;
    readonly VisualElement _modelColorDot;
    readonly Label _source;
    readonly Label _cost;
    readonly Label _hoverLine;
    readonly UkDropdown _presets;
    readonly UkField _search;
    readonly Button _apply;
    readonly Button _reset;
    readonly VisualElement _filterRow;
    readonly ScrollView _scroll;
    readonly UkPopover _colorPopover;
    readonly UkColorPicker _colorPicker;
    readonly UkSlider _boost;
    readonly VisualElement _boostHost;
    readonly Dictionary<string, Button> _filterButtons = new();
    readonly List<Cell> _cells = new();
    readonly Dictionary<IconCatalogEntry, string> _localizedNames = new();

    ItemDrop.ItemData? _item;
    PickMode _mode;
    IconCatalogEntry? _pendingIcon, _pendingModel;
    Color? _pendingIconTint, _pendingModelTint;
    bool _iconTintDirty, _modelTintDirty;
    bool _colorForModel;
    Color _tintBase = Color.white; // colour picker value; the model tint is base x boost
    float _tintBoost = 1f;
    string? _lastPreset;
    string _filter = FilterSameType;
    int _visibleCount, _unlockedCount;
    Action? _onClosed;

    public bool IsOpen => _window.IsOpen;

    bool HasPending => _pendingIcon != null || _pendingModel != null || _iconTintDirty || _modelTintDirty;
    bool CanIcon => CustomizeLibsAPI.CanReskinIcon(_item, Player.m_localPlayer);
    bool CanModel => CustomizeLibsAPI.CanReskinModel(_item, Player.m_localPlayer);
    bool CanColor => CustomizeLibsAPI.CanRecolor(_item, Player.m_localPlayer);

    public ToolkitLookPicker()
    {
        _window = new UkWindow("drakes_reskinit_picker", 980f);
        _window.Closed = () =>
        {
            ClosePopovers();
            _item = null;
            ClearPending();
            var closed = _onClosed;
            _onClosed = null;
            closed?.Invoke();
        };
        _window.EscapeHandler = () =>
        {
            if (!_colorPopover!.IsOpen)
                return false;
            _colorPopover.Close();
            return true;
        };

        var columns = new VisualElement().Row(Align.FlexStart);
        _window.Body.Add(columns);

        // ---------------- left column
        var left = new VisualElement().Column();
        left.style.width = 340;
        left.style.flexShrink = 0;
        left.style.marginRight = 22;
        columns.Add(left);

        var well = new VisualElement().Fill(UkTheme.Field).Round(UkTheme.Radius + 2).Border(UkTheme.LineSoft);
        well.style.height = 150;
        well.style.alignItems = Align.Center;
        well.style.justifyContent = Justify.Center;
        _bigIcon = new VisualElement { pickingMode = PickingMode.Ignore }.Size(104, 104);
        well.Add(_bigIcon);
        left.Add(well);

        _modeSegmented = new UkSegmented("Icon", "Model", "Both");
        _modeSegmented.Root.style.marginTop = 12;
        _modeSegmented.Changed = i => SetMode((PickMode)i);
        left.Add(_modeSegmented.Root);

        left.Add(PartRow("Icon", out _iconWas, out _iconNow, out _iconColorButton, out _iconColorDot, () => OpenColor(model: false)));
        _modelRow = PartRow("Model", out _modelWas, out _modelNow, out _modelColorButton, out _modelColorDot, () => OpenColor(model: true));
        left.Add(_modelRow);

        _source = UkControls.Text("", 13, UkTheme.TextMuted);
        _source.style.marginTop = 10;
        left.Add(_source);

        // Presets: pick one to load it into the preview; Save / Delete.
        var presetRow = new VisualElement().Row();
        presetRow.style.marginTop = 14;
        _presets = new UkDropdown(_window.Card, _window.Tips, 170f);
        _presets.Changed = LoadPreset;
        _presets.Root.style.marginRight = 8;
        presetRow.Add(_presets.Root);
        var save = UkControls.MakeButton("Save", SavePreset, UkButtonKind.Secondary);
        save.style.height = UkTheme.Target + 2;
        save.style.marginRight = 6;
        presetRow.Add(save);
        var delete = UkControls.MakeButton("Delete", DeletePreset, UkButtonKind.Secondary);
        delete.style.height = UkTheme.Target + 2;
        presetRow.Add(delete);
        left.Add(presetRow);

        // ---------------- right column
        var right = new VisualElement().Column().Grow();
        columns.Add(right);

        _filterRow = new VisualElement().Row(Align.FlexStart);
        _filterRow.style.flexWrap = Wrap.Wrap;
        foreach (var filter in Filters)
        {
            var captured = filter;
            var chip = UkControls.MakeButton(filter, () => SetFilter(captured), UkButtonKind.Ghost);
            chip.style.height = 32;
            chip.style.fontSize = 14;
            chip.style.paddingLeft = chip.style.paddingRight = 12;
            chip.style.marginRight = 4;
            chip.style.marginBottom = 4;
            _filterButtons[filter] = chip;
            _filterRow.Add(chip);
        }

        right.Add(_filterRow);

        _search = new UkField("Search", multiline: false, tips: _window.Tips);
        _search.Root.style.marginTop = 6;
        _search.Changed = _ => RefreshGrid();
        right.Add(_search.Root);

        _scroll = new ScrollView(ScrollViewMode.Vertical) { verticalScrollerVisibility = ScrollerVisibility.Hidden };
        _scroll.style.height = 300;
        _scroll.style.marginTop = 10;
        _scroll.Fill(UkTheme.Field).Round(UkTheme.Radius + 2).Border(UkTheme.LineSoft);
        _scroll.contentContainer.style.flexDirection = FlexDirection.Row;
        _scroll.contentContainer.style.flexWrap = Wrap.Wrap;
        _scroll.contentContainer.style.alignContent = Align.FlexStart;
        _scroll.contentContainer.style.paddingTop = _scroll.contentContainer.style.paddingBottom = 6;
        _scroll.contentContainer.style.paddingLeft = _scroll.contentContainer.style.paddingRight = 6;
        right.Add(_scroll);

        _hoverLine = UkControls.Text("", 14, UkTheme.TextMuted, wrap: false);
        _hoverLine.style.marginTop = 8;
        right.Add(_hoverLine);

        // ---------------- colour popover: a real picker, and (for the model) a brightness boost
        _colorPopover = new UkPopover(_window.Card, 330f);
        _colorPicker = new UkColorPicker { Picked = hex =>
        {
            if (UkColorPicker.TryParse(hex, out var color))
                SetPendingTint(_colorForModel, color);
        } };
        _colorPopover.Content.Add(_colorPicker.Root);
        _boostHost = new VisualElement().Column();
        _boostHost.style.marginTop = 12;
        _boost = new UkSlider("Brightness", 1f, ItemLookService.MaxTintBoost, 1f, v => "x" + v.ToString("0.0"));
        _boost.Changed = SetBoost;
        _boostHost.Add(_boost.Root);
        _colorPopover.Content.Add(_boostHost);
        var noColor = UkControls.MakeButton("No color", () => SetPendingTint(_colorForModel, Color.white), UkButtonKind.Secondary);
        noColor.style.marginTop = 12;
        _colorPopover.Content.Add(noColor);

        // A click anywhere else closes an open list or popover.
        _window.Card.RegisterCallback<PointerDownEvent>(e =>
        {
            var target = e.target as VisualElement;
            if (_presets.IsOpen && !_presets.Contains(target))
                _presets.Close();
            if (_colorPopover.IsOpen && !_colorPopover.Contains(target) && !_iconColorButton.Contains(target) && !_modelColorButton.Contains(target))
                _colorPopover.Close();
        }, TrickleDown.TrickleDown);

        // ---------------- footer
        _reset = UkControls.MakeButton("Reset", ResetLook, UkButtonKind.Secondary);
        _window.Footer.Add(_reset);
        _cost = UkControls.Text("", 14, UkTheme.TextMuted, wrap: false);
        _cost.style.marginLeft = 16;
        _cost.style.flexGrow = 1;
        _window.Footer.Add(_cost);
        var close = UkControls.MakeButton("Close", () => _window.Close(), UkButtonKind.Secondary);
        close.style.marginRight = 8;
        _window.Footer.Add(close);
        _apply = UkControls.MakeButton("Apply", Apply, UkButtonKind.Primary, 120f);
        _window.Footer.Add(_apply);
        _window.ShowFooter(true);
    }

    /// <summary>"Icon   [was] > [now]   Color": one part of the item.</summary>
    VisualElement PartRow(string label, out VisualElement was, out VisualElement now, out Button colorButton, out VisualElement dot, Action onColor)
    {
        var row = new VisualElement().Row();
        row.style.marginTop = 12;
        var name = UkControls.Text(label, 15, UkTheme.TextMuted, heading: true, wrap: false);
        name.style.width = 64;
        row.Add(name);
        was = SpriteTile(44);
        row.Add(was);

        // A drawn arrow: the font has none.
        var arrow = new VisualElement { pickingMode = PickingMode.Ignore }.Size(9, 9);
        arrow.style.borderRightWidth = arrow.style.borderBottomWidth = 2;
        arrow.style.borderRightColor = arrow.style.borderBottomColor = UkTheme.TextMuted;
        arrow.style.rotate = new Rotate(new Angle(-45f, AngleUnit.Degree));
        arrow.style.marginLeft = arrow.style.marginRight = 12;
        row.Add(arrow);

        now = SpriteTile(44);
        row.Add(now);
        row.Add(UkControls.Spacer());

        colorButton = UkControls.MakeButton("Color", onColor, UkButtonKind.Secondary);
        colorButton.style.height = 38;
        colorButton.style.flexDirection = FlexDirection.Row;
        colorButton.style.alignItems = Align.Center;
        colorButton.style.justifyContent = Justify.Center;
        dot = new VisualElement { pickingMode = PickingMode.Ignore }.Size(14, 14).Round(7).Border(new Color(1f, 1f, 1f, 0.3f));
        dot.style.marginLeft = 8;
        dot.style.backgroundColor = Color.white;
        colorButton.Add(dot);
        row.Add(colorButton);
        return row;
    }

    static VisualElement SpriteTile(float size)
    {
        var tile = new VisualElement { pickingMode = PickingMode.Ignore }.Size(size, size).Fixed().Fill(UkTheme.Field).Round(UkTheme.RadiusSmall).Border(UkTheme.LineSoft);
        var image = new VisualElement { pickingMode = PickingMode.Ignore };
        image.style.flexGrow = 1;
        image.style.marginLeft = image.style.marginRight = image.style.marginTop = image.style.marginBottom = 4;
        tile.Add(image);
        return tile;
    }

    // ── Open / close ────────────────────────────────────────────────────────

    public void Open(ItemDrop.ItemData item, Action? onClosed)
    {
        _item = item;
        _onClosed = onClosed;
        ClearPending();
        ClosePopovers();
        _filter = FilterSameType;
        _mode = CanIcon && CanModel ? PickMode.Both : CanModel ? PickMode.Model : PickMode.Icon;
        _search.SetValue("");

        RefreshControls();
        RefreshPresets();
        RefreshGrid();
        RefreshPreview();
        _window.ShowTabs(ReskinItPlugin.TabId);
        _window.Open();
    }

    /// <summary>Tab host switched away: hide without notifying.</summary>
    public void CloseSilent()
    {
        ClosePopovers();
        if (ResetConfirm.IsOpen)
            ResetConfirm.Close();
        if (PresetPrompt.IsOpen)
            PresetPrompt.Close();
        _item = null;
        ClearPending();
        _onClosed = null;
        _window.CloseSilent();
    }

    /// <summary>User closed (button / Escape / inventory closed): fires onClosed so the tab host releases.</summary>
    public void Close() => _window.Close();

    void ClosePopovers()
    {
        _colorPopover.Close();
        _presets.Close();
    }

    /// <summary>Per-frame guard from the plugin.</summary>
    public void Tick()
    {
        if (!IsOpen)
            return;
        if (!InventoryGui.IsVisible() || !ReskinItPlugin.IsInLocalInventory(_item))
        {
            Close();
            return;
        }

        if (Time.frameCount % 30 == 0)
            _cost.text = ReskinCost.Describe(Player.m_localPlayer);
    }

    // ── Actions ─────────────────────────────────────────────────────────────

    void SetMode(PickMode mode)
    {
        _mode = mode;
        // A pick that no longer applies to this mode is dropped.
        if (mode == PickMode.Icon) _pendingModel = null;
        if (mode == PickMode.Model) _pendingIcon = null;
        if (mode != PickMode.Icon)
            _filter = FilterSameType;
        RefreshControls();
        RefreshGrid();
        RefreshPreview();
    }

    void SetFilter(string filter)
    {
        _filter = filter;
        RefreshControls();
        RefreshGrid();
    }

    void Select(IconCatalogEntry entry)
    {
        if (_mode != PickMode.Model)
            _pendingIcon = entry;
        if (_mode != PickMode.Icon)
            _pendingModel = ItemLookService.IsModelCompatible(_item, entry) ? entry : null;
        RefreshSelectionOutline();
        RefreshPreview();
    }

    void OpenColor(bool model)
    {
        if (_item == null)
            return;
        if (!CanColor)
        {
            Say("Colors are turned off for this item");
            return;
        }

        // A boosted model tint is base color x boost: the picker edits the base, the slider the boost.
        var current = (model ? EffectiveModelTint() : EffectiveIconTint()) ?? Color.white;
        _colorForModel = model;
        _tintBoost = model ? ItemLookService.BoostOf(current) : 1f;
        _tintBase = new Color(current.r / _tintBoost, current.g / _tintBoost, current.b / _tintBoost, 1f);
        _colorPicker.SetColor(UkColorPicker.ToHex(_tintBase));
        _boostHost.Show(model);
        if (model)
            _boost.SetValueWithoutNotify(_tintBoost);
        _colorPopover.Toggle(model ? _modelColorButton : _iconColorButton);
    }

    void SetBoost(float boost)
    {
        _tintBoost = boost;
        SetPendingTint(model: true, _tintBase);
    }

    void SetPendingTint(bool model, Color c)
    {
        _tintBase = new Color(c.r, c.g, c.b, 1f);
        var boost = model ? _tintBoost : 1f;
        Color? tint = IsWhite(c) && boost < 1.005f ? null : new Color(c.r * boost, c.g * boost, c.b * boost, 1f);
        if (model)
        {
            _pendingModelTint = tint;
            _modelTintDirty = true;
        }
        else
        {
            _pendingIconTint = tint;
            _iconTintDirty = true;
        }

        RefreshPreview();
    }

    void Apply()
    {
        var player = Player.m_localPlayer;
        if (_item == null || player == null)
            return;
        if (!HasPending)
        {
            Say("Pick an icon, model or color first");
            return;
        }

        var unlocks = ReskinUnlocks.Begin(player);
        if (_pendingIcon != null)
        {
            if (!CanIcon) { Say("Icons can't be changed on this item"); return; }
            var locked = ReskinUnlocks.LockReason(_pendingIcon, unlocks);
            if (locked != null) { Say(locked); return; }
        }
        if (_pendingModel != null)
        {
            if (!CanModel || !ItemLookService.IsModelCompatible(_item, _pendingModel)) { Say("That model doesn't fit this item"); return; }
            var locked = ReskinUnlocks.LockReason(_pendingModel, unlocks);
            if (locked != null) { Say(locked); return; }
        }
        if ((_iconTintDirty || _modelTintDirty) && !CanColor)
        {
            Say("Colors are turned off for this item");
            return;
        }
        if (!ReskinCost.TryPay(player, out var error))
        {
            Say(error);
            return;
        }

        if (_pendingIcon != null) CustomizeLibsAPI.SetIconOverride(_item, _pendingIcon.SourceRef);
        if (_pendingModel != null) CustomizeLibsAPI.SetModelOverride(_item, _pendingModel.SourceRef);
        if (_iconTintDirty) CustomizeLibsAPI.SetIconTint(_item, _pendingIconTint);
        if (_modelTintDirty) CustomizeLibsAPI.SetModelTint(_item, _pendingModelTint);
        RefreshEquipment();
        ClearPending();
        _colorPopover.Close();
        Say("Reskinned");
        RefreshSelectionOutline();
        RefreshPreview();
    }

    void ResetLook()
    {
        if (_item == null)
            return;
        if (HasPending)
        {
            ClearPending();
            RefreshSelectionOutline();
            RefreshPreview();
            return;
        }
        if (!HasAppliedLook(_item))
            return;

        var item = _item;
        var body = ReskinItConfig.CostEnabled
            ? "Restore the original icon, model and colors? The reskin cost is not refunded."
            : "Restore the original icon, model and colors?";
        ResetConfirm.Show("Reset look", body, onYes: () =>
        {
            if (_item != item)
                return;
            if (CanIcon) CustomizeLibsAPI.ClearIconOverride(item);
            if (CanModel) CustomizeLibsAPI.SetModelOverride(item, null);
            if (CanColor)
            {
                CustomizeLibsAPI.SetIconTint(item, null);
                CustomizeLibsAPI.SetModelTint(item, null);
            }
            RefreshEquipment();
            Say("Original look restored");
            RefreshPreview();
        }, yesLabel: "Reset", noLabel: "Cancel");
    }

    // ── Presets (saved on this PC) ──────────────────────────────────────────

    void LoadPreset(int index)
    {
        if (_item == null || index <= 0 || index > ReskinPresets.All.Count)
            return;
        var preset = ReskinPresets.All[index - 1];
        _lastPreset = preset.Name;
        _presets.SetIndex(0);

        var unlocks = ReskinUnlocks.Begin(Player.m_localPlayer);
        var skipped = new List<string>();
        ClearPending();

        var icon = FindEntry(preset.IconRef);
        if (icon != null)
        {
            if (CanIcon && ReskinUnlocks.LockReason(icon, unlocks) == null) _pendingIcon = icon;
            else skipped.Add("icon");
        }
        var model = FindEntry(preset.ModelRef);
        if (model != null)
        {
            if (CanModel && ItemLookService.IsModelCompatible(_item, model) && ReskinUnlocks.LockReason(model, unlocks) == null) _pendingModel = model;
            else skipped.Add("model");
        }
        if (CanColor)
        {
            _pendingIconTint = preset.IconTint;
            _pendingModelTint = ItemLookService.IsEquippable(_item) ? preset.ModelTint : null;
            _iconTintDirty = _modelTintDirty = true;
        }
        else if (preset.IconTint != null || preset.ModelTint != null)
        {
            skipped.Add("colors");
        }

        Say(skipped.Count == 0
            ? $"Loaded \"{preset.Name}\". Press Apply to use it."
            : $"Loaded \"{preset.Name}\" without its {string.Join(" and ", skipped)} (doesn't fit or not unlocked).");
        RefreshSelectionOutline();
        RefreshPreview();
    }

    void SavePreset()
    {
        if (_item == null)
            return;
        var suggested = _lastPreset ?? CustomizeLibsAPI.GetDisplayNameForUi(_item, localize: true);
        PresetPrompt.Show("Save preset", suggested, name =>
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || _item == null)
                return;
            ReskinPresets.Save(new ReskinPreset
            {
                Name = name,
                IconRef = _pendingIcon?.SourceRef ?? CustomizeLibsAPI.GetIconOverride(_item),
                ModelRef = _pendingModel?.SourceRef ?? CustomizeLibsAPI.GetModelOverride(_item),
                IconTint = EffectiveIconTint(),
                ModelTint = EffectiveModelTint(),
            });
            _lastPreset = name;
            RefreshPresets();
            Say($"Saved preset \"{name}\"");
        }, charLimit: 32, okLabel: "Save");
    }

    void DeletePreset()
    {
        if (_lastPreset == null || ReskinPresets.Find(_lastPreset) == null)
        {
            Say("Load a preset first, then Delete removes it");
            return;
        }

        var name = _lastPreset;
        ResetConfirm.Show("Delete preset", $"Delete the preset \"{name}\"?", onYes: () =>
        {
            ReskinPresets.Delete(name);
            _lastPreset = null;
            RefreshPresets();
            Say($"Deleted \"{name}\"");
        }, yesLabel: "Delete", noLabel: "Cancel");
    }

    // ── State helpers ───────────────────────────────────────────────────────

    void ClearPending()
    {
        _pendingIcon = _pendingModel = null;
        _pendingIconTint = _pendingModelTint = null;
        _iconTintDirty = _modelTintDirty = false;
    }

    Color? EffectiveIconTint() => _iconTintDirty ? _pendingIconTint : CustomizeLibsAPI.GetIconTint(_item);
    Color? EffectiveModelTint() => _modelTintDirty ? _pendingModelTint : CustomizeLibsAPI.GetModelTint(_item);

    static bool HasAppliedLook(ItemDrop.ItemData item) =>
        CustomizeLibsAPI.HasIconOverride(item) || CustomizeLibsAPI.GetModelOverride(item) != null
        || CustomizeLibsAPI.GetIconTint(item) != null || CustomizeLibsAPI.GetModelTint(item) != null;

    void RefreshEquipment()
    {
        // Re-run equipment visuals so model swaps and model tints show right away (and sync to others).
        // Humanoid.SetupEquipment is protected in the real game: call it through reflection.
        if (_item != null && _item.m_equipped && Player.m_localPlayer != null)
            SetupEquipmentMethod?.Invoke(Player.m_localPlayer, null);
    }

    static bool IsWhite(Color c) => c.r > 0.99f && c.g > 0.99f && c.b > 0.99f;

    /// <summary>A short message in the window itself (the HUD message would sit under the dimmed screen).</summary>
    void Say(string message) => _window.Toast.Show(message, null, 4f);

    IconCatalogEntry? FindEntry(string? sourceRef) =>
        string.IsNullOrEmpty(sourceRef) ? null : CustomizeLibsAPI.GetIconCatalog().FirstOrDefault(e => e.SourceRef == sourceRef);

    // ── Refresh ─────────────────────────────────────────────────────────────

    void RefreshControls()
    {
        var equippable = ItemLookService.IsEquippable(_item);
        _modeSegmented.Root.Show(CanIcon && CanModel);
        _modeSegmented.SetIndex((int)_mode);

        _modelRow.Show(equippable && (CanModel || CanColor));
        _iconColorButton.Show(CanColor);
        _modelColorButton.Show(CanColor && equippable);

        // Models only fit the same slot, so Model/Both lock the grid to Same type.
        var lockToType = _mode != PickMode.Icon || ReskinItConfig.SameTypeOnly;
        foreach (var kv in _filterButtons)
        {
            kv.Value.Show(!lockToType || kv.Key == FilterSameType);
            UkControls.SetKind(kv.Value, kv.Key == _filter ? UkButtonKind.Selected : UkButtonKind.Ghost);
        }
    }

    void RefreshPreview()
    {
        if (_item == null)
            return;
        var original = OriginalIcon(_item);
        var iconSprite = _pendingIcon?.Sprite ?? _item.GetIcon();
        var iconTint = EffectiveIconTint() ?? Color.white;
        var modelEntry = _pendingModel ?? FindEntry(CustomizeLibsAPI.GetModelOverride(_item));
        var modelTint = EffectiveModelTint() ?? Color.white;

        SetSprite(_bigIcon, iconSprite, iconTint);
        SetSprite(_iconWas, original, Color.white);
        SetSprite(_iconNow, iconSprite, iconTint);
        SetSprite(_modelWas, original, Color.white);
        SetSprite(_modelNow, modelEntry?.Sprite ?? original, modelTint);
        _iconColorDot.style.backgroundColor = iconTint;
        _modelColorDot.style.backgroundColor = modelTint;

        var name = CustomizeLibsAPI.GetDisplayNameForUi(_item, localize: true);
        var badge = HasPending ? "Preview" : HasAppliedLook(_item) ? "Reskinned" : "Original";
        _window.SetHeader(name, badge, iconSprite);
        _source.text = DescribeLook(modelEntry);
        _cost.text = ReskinCost.Describe(Player.m_localPlayer);
        UkControls.SetButtonEnabled(_apply, HasPending);
    }

    string DescribeLook(IconCatalogEntry? modelEntry)
    {
        var iconEntry = _pendingIcon ?? FindEntry(CustomizeLibsAPI.GetIconOverride(_item));
        var parts = new List<string>();
        if (iconEntry != null) parts.Add($"Icon: {NameOf(iconEntry)}");
        if (modelEntry != null) parts.Add($"Model: {NameOf(modelEntry)}");
        return parts.Count == 0 ? "No change" : string.Join("\n", parts);
    }

    void RefreshPresets()
    {
        var options = new List<string> { ReskinPresets.All.Count == 0 ? "No presets" : "Presets..." };
        options.AddRange(ReskinPresets.All.Select(p => p.Name));
        _presets.SetOptions(options, 0);
    }

    void RefreshGrid()
    {
        if (_item == null)
            return;

        var sameType = IconCatalog.CategoryOf(_item);
        var unlocks = ReskinUnlocks.Begin(Player.m_localPlayer);
        var query = (_search.Value ?? "").Trim();
        var entries = CustomizeLibsAPI.GetIconCatalog()
            .Where(e => MatchesFilter(e, sameType))
            .Where(e => query.Length == 0
                        || NameOf(e).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                        || e.PrefabName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(e => e.Category)
            .ThenBy(NameOf, StringComparer.OrdinalIgnoreCase)
            .Select(e => (Entry: e, Lock: ReskinUnlocks.LockReason(e, unlocks)))
            .Where(x => x.Lock == null || ReskinItConfig.ShowUndiscovered)
            .ToList();

        while (_cells.Count < entries.Count)
            _cells.Add(CreateCell());

        for (var i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            if (i >= entries.Count)
            {
                cell.Entry = null;
                cell.Root.Show(false);
                continue;
            }

            cell.Entry = entries[i].Entry;
            cell.Lock = entries[i].Lock;
            SetSprite(cell.Icon, cell.Entry.Sprite, cell.Lock == null ? Color.white : LockedIconColor);
            cell.Root.style.backgroundColor = cell.Lock == null ? SlotColor : LockedSlotColor;
            cell.Root.Show(true);
        }

        _visibleCount = entries.Count;
        _unlockedCount = entries.Count(x => x.Lock == null);
        RefreshSelectionOutline();
        ShowCountLine();
        _scroll.scrollOffset = Vector2.zero;
    }

    bool MatchesFilter(IconCatalogEntry e, IconCategory sameType)
    {
        if (_mode != PickMode.Icon)
            return ItemLookService.IsModelCompatible(_item, e);
        if (ReskinItConfig.SameTypeOnly || _filter == FilterSameType)
            return e.Category == sameType;
        return _filter == FilterAll || e.Category.ToString() == _filter;
    }

    void RefreshSelectionOutline()
    {
        foreach (var cell in _cells)
        {
            var selected = cell.Entry != null && (cell.Entry == _pendingIcon || cell.Entry == _pendingModel);
            cell.Root.Border(selected ? UkTheme.Accent : UkTheme.LineSoft, selected ? 2f : 1f);
        }
    }

    void ShowCountLine()
    {
        var scope = _mode != PickMode.Icon
            ? " (models that fit this slot)"
            : _filter == FilterSameType || ReskinItConfig.SameTypeOnly
                ? $" ({IconCatalog.CategoryOf(_item).ToString().ToLowerInvariant()})"
                : "";
        _hoverLine.text = _unlockedCount == _visibleCount
            ? $"{_visibleCount} looks{scope}"
            : $"{_unlockedCount} / {_visibleCount} unlocked{scope}";
    }

    string NameOf(IconCatalogEntry e)
    {
        if (_localizedNames.TryGetValue(e, out var cached))
            return cached;
        var name = Localization.instance?.Localize(e.NameToken) ?? e.NameToken;
        if (e.Variant > 0)
            name += $" ({e.Variant + 1})";
        _localizedNames[e] = name;
        return name;
    }

    static Sprite? OriginalIcon(ItemDrop.ItemData item)
    {
        var icons = item.m_shared?.m_icons;
        if (icons == null || icons.Length == 0)
            return null;
        return icons[Mathf.Clamp(item.m_variant, 0, icons.Length - 1)];
    }

    /// <summary>Puts a sprite (tinted) into a tile; null clears it.</summary>
    static void SetSprite(VisualElement element, Sprite? sprite, Color tint)
    {
        // A tile holds its image in its first child; a bare element is the image itself.
        var image = element.childCount > 0 && element[0].pickingMode == PickingMode.Ignore ? element[0] : element;
        if (sprite == null)
        {
            image.style.backgroundImage = StyleKeyword.None;
            return;
        }

        image.style.backgroundImage = new StyleBackground(sprite);
        image.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        image.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
        image.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
        image.style.unityBackgroundImageTintColor = tint;
    }

    // ── Grid cells ──────────────────────────────────────────────────────────

    Cell CreateCell()
    {
        var root = new VisualElement().Size(CellSize, CellSize).Fixed().Fill(SlotColor).Round(UkTheme.RadiusSmall).Border(UkTheme.LineSoft);
        root.style.marginRight = root.style.marginBottom = 4;
        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.style.flexGrow = 1;
        icon.style.marginLeft = icon.style.marginRight = icon.style.marginTop = icon.style.marginBottom = 5;
        root.Add(icon);

        var cell = new Cell { Root = root, Icon = icon };
        root.RegisterCallback<ClickEvent>(_ =>
        {
            if (cell.Entry == null)
                return;
            if (cell.Lock != null)
            {
                Say(cell.Lock);
                return;
            }

            Select(cell.Entry);
        });
        root.RegisterCallback<PointerEnterEvent>(_ =>
        {
            if (cell.Lock == null)
                root.style.backgroundColor = SlotHoverColor;
            if (cell.Entry != null)
                _hoverLine.text = cell.Lock == null ? NameOf(cell.Entry) : $"{NameOf(cell.Entry)}  <color=#c08060>{cell.Lock}</color>";
        });
        root.RegisterCallback<PointerLeaveEvent>(_ =>
        {
            root.style.backgroundColor = cell.Lock == null ? SlotColor : LockedSlotColor;
            ShowCountLine();
        });
        _scroll.contentContainer.Add(root);
        return cell;
    }

    sealed class Cell
    {
        public VisualElement Root = null!;
        public VisualElement Icon = null!;
        public IconCatalogEntry? Entry;
        public string? Lock;
    }
}
