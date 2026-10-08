using System;
using System.Collections.Generic;
using System.Linq;
using DrakeModsLibs.API;
using DrakeModsLibs.Display;
using DrakeModsLibs.UI;
using Jotunn.GUI;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Text = UnityEngine.UI.Text;

namespace DrakesReskinIt.UI;

/// <summary>
/// Build-menu style look picker. Left: mode (Icon / Model / Both), preview, per-part rows with a Color button each,
/// presets, cost, actions. Right: category tabs + search + grid (Model/Both lock the grid to the item's own slot type).
/// Nothing is written to the item until Apply; one Apply charges the cost once.
/// </summary>
internal sealed class LookPickerPanel
{
    enum PickMode { Icon, Model, Both }

    const float PanelWidth = 720f;
    const float PanelHeight = 400f;
    const float PanelY = -20f;
    const float LeftX = -238f;
    const float RightX = 110f;
    const float GridWidth = 460f;
    const float GridHeight = 236f;
    const float GridCenterY = -26f;
    const float CellSize = 44f;
    const float CellGap = 4f;

    const string FilterAll = "All";
    const string FilterSameType = "Same type";

    static readonly Color SlotColor = new Color(0.17f, 0.13f, 0.10f, 0.95f);
    static readonly Color SlotHoverColor = new Color(0.27f, 0.21f, 0.16f, 0.95f);
    static readonly Color WellColor = new Color(0.11f, 0.09f, 0.07f, 0.9f);
    static readonly Color MutedText = new Color(0.72f, 0.65f, 0.50f);
    static readonly Color LockedSlotColor = new Color(0.10f, 0.08f, 0.06f, 0.95f);
    static readonly Color LockedIconColor = new Color(0f, 0f, 0f, 0.85f); // silhouette, like vanilla unknown recipes

    static readonly string[] Filters =
        new[] { FilterAll, FilterSameType }.Concat(Enum.GetNames(typeof(IconCategory))).ToArray();

    static readonly System.Reflection.MethodInfo? SetupEquipmentMethod =
        HarmonyLib.AccessTools.Method(typeof(Humanoid), "SetupEquipment");

    static readonly DrakeConfirmPanel ResetConfirm = new DrakeConfirmPanel("drakes_reskinit_reset_confirm", null);
    static readonly DrakeTextPromptPanel PresetPrompt = new DrakeTextPromptPanel("drakes_reskinit_preset_prompt");

    GameObject? _panel;
    Text? _itemName, _badge, _source, _cost, _hover;
    Image? _bigIcon, _iconWas, _iconNow, _modelWas, _modelNow;
    GameObject? _modelRow;
    Button? _iconColorButton, _modelColorButton;
    InputField? _search;
    Dropdown? _presets;
    RectTransform? _gridContent;
    ScrollRect? _scroll;
    readonly Dictionary<PickMode, Button> _modeButtons = new Dictionary<PickMode, Button>();
    readonly Dictionary<string, Button> _filterButtons = new Dictionary<string, Button>();
    readonly List<Cell> _cells = new List<Cell>();
    readonly Dictionary<IconCatalogEntry, string> _localizedNames = new Dictionary<IconCatalogEntry, string>();

    ItemDrop.ItemData? _item;
    PickMode _mode;
    IconCatalogEntry? _pendingIcon, _pendingModel;
    Color? _pendingIconTint, _pendingModelTint;
    bool _iconTintDirty, _modelTintDirty;
    bool _pickerOpen;
    DrakeSlider? _boost;          // brightness boost, shown under the model color picker while it's open
    Color _tintBase = Color.white; // color picker value; the model tint is base x boost
    float _tintBoost = 1f;
    string? _lastPreset;
    string _filter = FilterSameType;
    int _visibleCount, _unlockedCount;
    Action? _onClosed;

    public bool IsOpen => _panel && _panel!.activeSelf;

    bool HasPending => _pendingIcon != null || _pendingModel != null || _iconTintDirty || _modelTintDirty;
    bool CanIcon => CustomizeLibsAPI.CanReskinIcon(_item, Player.m_localPlayer);
    bool CanModel => CustomizeLibsAPI.CanReskinModel(_item, Player.m_localPlayer);
    bool CanColor => CustomizeLibsAPI.CanRecolor(_item, Player.m_localPlayer);

    public void Open(ItemDrop.ItemData item, Action? onClosed)
    {
        if (GUIManager.Instance == null || !GUIManager.CustomGUIFront)
            return;
        EnsurePanel();
        if (!_panel)
            return;

        _item = item;
        _onClosed = onClosed;
        ClearPending();
        _filter = FilterSameType;
        _mode = CanIcon && CanModel ? PickMode.Both : CanModel ? PickMode.Model : PickMode.Icon;
        if (_search)
            _search!.SetTextWithoutNotify("");

        RefreshControls();
        RefreshPresets();
        _panel!.SetActive(true);
        _panel.transform.SetAsLastSibling();
        DrakeGuiInput.EnsureBlocked();
        RefreshGrid();
        RefreshPreview();
    }

    /// <summary>User closed (button / Escape / inventory closed): fires onClosed so the tab host releases.</summary>
    public void Close() => Close(invokeClosed: true);

    /// <summary>Tab host switched away: hide without notifying.</summary>
    public void CloseSilent() => Close(invokeClosed: false);

    void Close(bool invokeClosed)
    {
        if (_pickerOpen && !ColorPicker.done)
            ColorPicker.Cancel();
        _pickerOpen = false;
        ShowBoost(false);
        if (ResetConfirm.IsOpen)
            ResetConfirm.Close();
        if (PresetPrompt.IsOpen)
            PresetPrompt.Close();
        if (_panel)
            _panel!.SetActive(false);
        _item = null;
        ClearPending();
        DrakeGuiInput.EnsureUnblocked();
        if (!invokeClosed)
            return;
        var closed = _onClosed;
        _onClosed = null;
        closed?.Invoke();
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
        if (_pickerOpen && ColorPicker.done)
        {
            _pickerOpen = false;
            ShowBoost(false);
        }

        // Escape belongs to whatever is on top: color picker, confirm, or name prompt.
        if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
        {
            if (_pickerOpen)
                ColorPicker.Cancel();
            else if (!ResetConfirm.IsOpen && !PresetPrompt.IsOpen)
                Close();
            return;
        }

        if (_cost != null && Time.frameCount % 30 == 0)
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
        if (_item == null || _pickerOpen)
            return;
        if (!CanColor)
        {
            Toast("Colors are turned off for this item");
            return;
        }
        // A boosted model tint is base color x boost: the picker edits the base, the slider the boost.
        var current = (model ? EffectiveModelTint() : EffectiveIconTint()) ?? Color.white;
        _tintBoost = model ? ItemLookService.BoostOf(current) : 1f;
        _tintBase = new Color(current.r / _tintBoost, current.g / _tintBoost, current.b / _tintBoost, 1f);
        _pickerOpen = true;
        GUIManager.Instance.CreateColorPicker(
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(RightX, PanelY),
            _tintBase, model ? "Model color (white = none)" : "Icon color (white = none)",
            c => SetPendingTint(model, c),
            c =>
            {
                SetPendingTint(model, c);
                _pickerOpen = false;
                ShowBoost(false);
                DrakeGuiInput.EnsureBlocked();
            },
            false);
        if (model)
        {
            _boost?.SetValueWithoutNotify(_tintBoost);
            ShowBoost(true);
        }
    }

    void ShowBoost(bool show)
    {
        // Sits on the count line under the grid, which the color picker leaves uncovered.
        if (_boost != null)
            _boost.Root.SetActive(show);
        if (_hover != null)
            _hover.gameObject.SetActive(!show);
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
            Toast("Pick an icon, model or color first");
            return;
        }

        var unlocks = ReskinUnlocks.Begin(player);
        if (_pendingIcon != null)
        {
            if (!CanIcon) { Toast("Icons can't be changed on this item"); return; }
            var locked = ReskinUnlocks.LockReason(_pendingIcon, unlocks);
            if (locked != null) { Toast(locked); return; }
        }
        if (_pendingModel != null)
        {
            if (!CanModel || !ItemLookService.IsModelCompatible(_item, _pendingModel)) { Toast("That model doesn't fit this item"); return; }
            var locked = ReskinUnlocks.LockReason(_pendingModel, unlocks);
            if (locked != null) { Toast(locked); return; }
        }
        if ((_iconTintDirty || _modelTintDirty) && !CanColor)
        {
            Toast("Colors are turned off for this item");
            return;
        }
        if (!ReskinCost.TryPay(player, out var error))
        {
            Toast(error);
            return;
        }

        if (_pendingIcon != null) CustomizeLibsAPI.SetIconOverride(_item, _pendingIcon.SourceRef);
        if (_pendingModel != null) CustomizeLibsAPI.SetModelOverride(_item, _pendingModel.SourceRef);
        if (_iconTintDirty) CustomizeLibsAPI.SetIconTint(_item, _pendingIconTint);
        if (_modelTintDirty) CustomizeLibsAPI.SetModelTint(_item, _pendingModelTint);
        RefreshEquipment();
        ClearPending();
        Toast("Reskinned");
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
            DrakeGuiInput.EnsureBlocked(); // picker is still open under the confirm
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
            Toast("Original look restored");
            RefreshPreview();
        }, onNo: DrakeGuiInput.EnsureBlocked, yesLabel: "Reset", noLabel: "Cancel");
    }

    // ── Presets (saved on this PC) ──────────────────────────────────────────

    void LoadPreset(int index)
    {
        if (_item == null || index <= 0 || index > ReskinPresets.All.Count)
            return;
        var preset = ReskinPresets.All[index - 1];
        _lastPreset = preset.Name;
        _presets?.SetValueWithoutNotify(0);

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

        Toast(skipped.Count == 0
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
            DrakeGuiInput.EnsureBlocked();
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
            Toast($"Saved preset \"{name}\"");
        }, onCancel: DrakeGuiInput.EnsureBlocked, charLimit: 32, okLabel: "Save");
    }

    void DeletePreset()
    {
        if (_lastPreset == null || ReskinPresets.Find(_lastPreset) == null)
        {
            Toast("Load a preset first, then Delete removes it");
            return;
        }
        var name = _lastPreset;
        ResetConfirm.Show("Delete preset", $"Delete the preset \"{name}\"?", onYes: () =>
        {
            DrakeGuiInput.EnsureBlocked();
            ReskinPresets.Delete(name);
            _lastPreset = null;
            RefreshPresets();
            Toast($"Deleted \"{name}\"");
        }, onNo: DrakeGuiInput.EnsureBlocked, yesLabel: "Delete", noLabel: "Cancel");
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

    static void Toast(string msg) => CustomizeLibsAPI.ShowHudMessage(Player.m_localPlayer, MessageHud.MessageType.Center, msg);

    IconCatalogEntry? FindEntry(string? sourceRef) =>
        string.IsNullOrEmpty(sourceRef) ? null : CustomizeLibsAPI.GetIconCatalog().FirstOrDefault(e => e.SourceRef == sourceRef);

    // ── Refresh ─────────────────────────────────────────────────────────────

    void RefreshControls()
    {
        var equippable = ItemLookService.IsEquippable(_item);
        SetVisible(_modeButtons[PickMode.Icon], CanIcon && CanModel);
        SetVisible(_modeButtons[PickMode.Model], CanIcon && CanModel);
        SetVisible(_modeButtons[PickMode.Both], CanIcon && CanModel);
        foreach (var kv in _modeButtons)
            SetLabelColor(kv.Value, kv.Key == _mode ? GUIManager.Instance.ValheimOrange : Color.white);

        if (_modelRow)
            _modelRow!.SetActive(equippable && (CanModel || CanColor));
        if (_iconColorButton)
            _iconColorButton!.gameObject.SetActive(CanColor);
        if (_modelColorButton)
            _modelColorButton!.gameObject.SetActive(CanColor && equippable);

        // Models only fit the same slot, so Model/Both lock the grid to Same type.
        var lockToType = _mode != PickMode.Icon || ReskinItConfig.SameTypeOnly;
        foreach (var kv in _filterButtons)
        {
            kv.Value.gameObject.SetActive(!lockToType || kv.Key == FilterSameType);
            SetLabelColor(kv.Value, kv.Key == _filter ? GUIManager.Instance.ValheimOrange : Color.white);
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
        SetLabelColor(_iconColorButton, EffectiveIconTint() ?? Color.white);
        SetLabelColor(_modelColorButton, EffectiveModelTint() ?? Color.white);

        if (_itemName != null)
            _itemName.text = CustomizeLibsAPI.GetDisplayNameForUi(_item, localize: true);
        if (_badge != null)
            _badge.text = HasPending ? "Preview" : HasAppliedLook(_item) ? "Reskinned" : "Original";
        if (_source != null)
            _source.text = DescribeLook(modelEntry);
        if (_cost != null)
            _cost.text = ReskinCost.Describe(Player.m_localPlayer);
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
        if (_presets == null)
            return;
        _presets.ClearOptions();
        var options = new List<string> { ReskinPresets.All.Count == 0 ? "No presets" : "Presets…" };
        options.AddRange(ReskinPresets.All.Select(p => p.Name));
        _presets.AddOptions(options);
        _presets.SetValueWithoutNotify(0);
    }

    void RefreshGrid()
    {
        if (_gridContent == null || _item == null)
            return;

        var sameType = IconCatalog.CategoryOf(_item);
        var unlocks = ReskinUnlocks.Begin(Player.m_localPlayer);
        var query = _search ? _search!.text.Trim() : "";
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
            _cells.Add(CreateCell(_gridContent));

        for (var i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            if (i >= entries.Count)
            {
                cell.Entry = null;
                cell.Go.SetActive(false);
                continue;
            }
            cell.Entry = entries[i].Entry;
            cell.Lock = entries[i].Lock;
            cell.Icon.sprite = cell.Entry.Sprite;
            cell.Icon.color = cell.Lock == null ? Color.white : LockedIconColor;
            cell.Bg.color = cell.Lock == null ? SlotColor : LockedSlotColor;
            cell.Go.SetActive(true);
        }

        _visibleCount = entries.Count;
        _unlockedCount = entries.Count(x => x.Lock == null);
        RefreshSelectionOutline();
        ShowCountLine();
        if (_scroll)
            _scroll!.verticalNormalizedPosition = 1f;
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
            cell.Outline.enabled = cell.Entry != null && (cell.Entry == _pendingIcon || cell.Entry == _pendingModel);
    }

    void ShowCountLine()
    {
        if (_hover == null)
            return;
        var scope = _mode != PickMode.Icon
            ? " (models that fit this slot)"
            : _filter == FilterSameType || ReskinItConfig.SameTypeOnly
                ? $" ({IconCatalog.CategoryOf(_item).ToString().ToLowerInvariant()})"
                : "";
        _hover.text = _unlockedCount == _visibleCount
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

    static void SetSprite(Image? img, Sprite? sprite, Color tint)
    {
        if (img == null)
            return;
        img.sprite = sprite;
        img.color = tint;
        img.enabled = sprite;
    }

    static void SetVisible(Button? b, bool visible)
    {
        if (b)
            b!.gameObject.SetActive(visible);
    }

    static void SetLabelColor(Button? b, Color c)
    {
        var label = b ? b!.GetComponentInChildren<Text>(true) : null;
        if (label)
            label!.color = c;
    }

    // ── Build ───────────────────────────────────────────────────────────────

    void EnsurePanel()
    {
        if (_panel || GUIManager.Instance == null || !GUIManager.CustomGUIFront)
            return;

        var gui = GUIManager.Instance;
        _panel = gui.CreateWoodpanel(
            parent: GUIManager.CustomGUIFront.transform,
            anchorMin: new Vector2(0.5f, 0.5f),
            anchorMax: new Vector2(0.5f, 0.5f),
            position: new Vector2(0f, PanelY),
            width: PanelWidth,
            height: PanelHeight,
            draggable: false);
        _panel.name = "drakes_reskinit_picker";
        var root = _panel.transform;

        // Left: name, mode, preview.
        _itemName = MakeText("", root, new Vector2(LeftX, 176f), 20, gui.ValheimOrange, 210f, 28f, TextAnchor.MiddleCenter);
        _modeButtons[PickMode.Icon] = MakeButton(root, "Icon", new Vector2(LeftX - 68f, 150f), 64f, 22f, () => SetMode(PickMode.Icon), 12);
        _modeButtons[PickMode.Model] = MakeButton(root, "Model", new Vector2(LeftX, 150f), 64f, 22f, () => SetMode(PickMode.Model), 12);
        _modeButtons[PickMode.Both] = MakeButton(root, "Both", new Vector2(LeftX + 68f, 150f), 64f, 22f, () => SetMode(PickMode.Both), 12);

        MakeImage("preview_well", root, new Vector2(LeftX, 78f), new Vector2(150f, 116f), WellColor);
        _bigIcon = MakeImage("preview_icon", root, new Vector2(LeftX, 78f), new Vector2(88f, 88f), Color.white);
        _bigIcon.preserveAspect = true;
        _badge = MakeText("", root, new Vector2(LeftX - 32f, 128f), 12, MutedText, 80f, 18f, TextAnchor.MiddleLeft);

        // Icon row and model row: label, before -> after, Color button.
        BuildPartRow(root, "Icon", 2f, out _iconWas, out _iconNow, out _iconColorButton, () => OpenColor(model: false));
        var modelRow = new GameObject("model_row", typeof(RectTransform));
        modelRow.transform.SetParent(root, false);
        _modelRow = modelRow;
        BuildPartRow(modelRow.transform, "Model", -40f, out _modelWas, out _modelNow, out _modelColorButton, () => OpenColor(model: true));

        _source = MakeText("", root, new Vector2(LeftX, -76f), 12, MutedText, 210f, 30f, TextAnchor.MiddleCenter);

        // Presets: dropdown loads into the preview; Save / Delete.
        var dropGo = gui.CreateDropDown(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(LeftX - 50f, -108f), 13, 106f, 26f);
        _presets = dropGo.GetComponent<Dropdown>();
        if (_presets != null)
            _presets.onValueChanged.AddListener(LoadPreset);
        MakeButton(root, "Save", new Vector2(LeftX + 30f, -108f), 48f, 26f, SavePreset, 12);
        MakeButton(root, "Delete", new Vector2(LeftX + 82f, -108f), 52f, 26f, DeletePreset, 12);

        _cost = MakeText("", root, new Vector2(LeftX, -138f), 13, Color.white, 220f, 20f, TextAnchor.MiddleCenter);

        MakeButton(root, "Reset", new Vector2(LeftX - 68f, -170f), 60f, 30f, ResetLook);
        MakeButton(root, "Apply", new Vector2(LeftX, -170f), 68f, 30f, Apply);
        MakeButton(root, "Close", new Vector2(LeftX + 68f, -170f), 60f, 30f, Close);

        // Right: category tabs (two rows), search, grid.
        for (var i = 0; i < Filters.Length; i++)
        {
            var row = i < 5 ? 0 : 1;
            var col = row == 0 ? i : i - 5;
            var perRow = row == 0 ? 5 : Filters.Length - 5;
            var x = RightX + (col - (perRow - 1) / 2f) * 92f;
            var filter = Filters[i];
            _filterButtons[filter] = MakeButton(root, filter, new Vector2(x, 176f - row * 28f), 88f, 24f, () => SetFilter(filter), 12);
        }

        var searchGo = gui.CreateInputField(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(RightX, 114f), InputField.ContentType.Standard, "Search", 14, GridWidth, 26f);
        _search = searchGo.GetComponent<InputField>();
        _search.onValueChanged.AddListener(_ => RefreshGrid());

        BuildGrid(root);
        _hover = MakeText("", root, new Vector2(RightX, -160f), 13, MutedText, GridWidth, 20f, TextAnchor.MiddleLeft);

        // Brightness boost for the model tint: above 1 pushes dark textures toward white. Shown with the model color picker.
        _boost = DrakeSlider.Create(root, new Vector2(RightX, -160f), "Brightness", 1f, ItemLookService.MaxTintBoost, 1f,
            SetBoost, v => "x" + v.ToString("0.0"), width: GridWidth - 40f);
        _boost?.Root.SetActive(false);

        DrakeButtonSfx.Soften(_panel);
        _panel.SetActive(false);
    }

    static void BuildPartRow(Transform parent, string label, float y, out Image was, out Image now, out Button colorButton, UnityAction onColor)
    {
        MakeText(label, parent, new Vector2(LeftX - 78f, y), 13, MutedText, 50f, 20f, TextAnchor.MiddleLeft);
        MakeImage(label + "_was_well", parent, new Vector2(LeftX - 30f, y), new Vector2(36f, 36f), WellColor);
        was = MakeImage(label + "_was", parent, new Vector2(LeftX - 30f, y), new Vector2(30f, 30f), Color.white);
        was.preserveAspect = true;
        MakeText("→", parent, new Vector2(LeftX - 4f, y), 18, MutedText, 20f, 24f, TextAnchor.MiddleCenter);
        MakeImage(label + "_now_well", parent, new Vector2(LeftX + 22f, y), new Vector2(36f, 36f), WellColor);
        now = MakeImage(label + "_now", parent, new Vector2(LeftX + 22f, y), new Vector2(30f, 30f), Color.white);
        now.preserveAspect = true;
        colorButton = MakeButton(parent, "Color", new Vector2(LeftX + 76f, y), 58f, 26f, onColor, 12);
    }

    void BuildGrid(Transform root)
    {
        var well = MakeImage("grid_well", root, new Vector2(RightX, GridCenterY), new Vector2(GridWidth, GridHeight), WellColor);
        well.gameObject.AddComponent<RectMask2D>();

        var content = new GameObject("grid_content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(well.transform, false);
        _gridContent = (RectTransform)content.transform;
        _gridContent.anchorMin = new Vector2(0f, 1f);
        _gridContent.anchorMax = new Vector2(1f, 1f);
        _gridContent.pivot = new Vector2(0.5f, 1f);
        _gridContent.anchoredPosition = Vector2.zero;
        _gridContent.sizeDelta = Vector2.zero;

        var grid = content.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(CellSize, CellSize);
        grid.spacing = new Vector2(CellGap, CellGap);
        grid.padding = new RectOffset(6, 6, 6, 6);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Mathf.FloorToInt((GridWidth - 12f + CellGap) / (CellSize + CellGap));
        grid.childAlignment = TextAnchor.UpperCenter;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _scroll = well.gameObject.AddComponent<ScrollRect>();
        _scroll.content = _gridContent;
        _scroll.viewport = (RectTransform)well.transform;
        _scroll.horizontal = false;
        _scroll.vertical = true;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 30f;
    }

    Cell CreateCell(Transform parent)
    {
        var go = new GameObject("look_cell", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline), typeof(EventTrigger));
        go.transform.SetParent(parent, false);
        var cell = new Cell { Go = go, Bg = go.GetComponent<Image>(), Outline = go.GetComponent<Outline>() };
        cell.Bg.color = SlotColor;
        cell.Outline.effectColor = GUIManager.Instance.ValheimOrange;
        cell.Outline.effectDistance = new Vector2(2f, -2f);
        cell.Outline.enabled = false;

        var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(go.transform, false);
        var iconRt = (RectTransform)iconGo.transform;
        iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 0.5f);
        iconRt.sizeDelta = new Vector2(CellSize - 8f, CellSize - 8f);
        cell.Icon = iconGo.GetComponent<Image>();
        cell.Icon.preserveAspect = true;
        cell.Icon.raycastTarget = false;

        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() =>
        {
            if (cell.Entry == null)
                return;
            if (cell.Lock != null)
            {
                Toast(cell.Lock);
                return;
            }
            Select(cell.Entry);
        });

        var trigger = go.GetComponent<EventTrigger>();
        AddTrigger(trigger, EventTriggerType.PointerEnter, () =>
        {
            if (cell.Lock == null)
                cell.Bg.color = SlotHoverColor;
            if (_hover != null && cell.Entry != null)
                _hover.text = cell.Lock == null ? NameOf(cell.Entry) : $"{NameOf(cell.Entry)}  <color=#c08060>{cell.Lock}</color>";
        });
        AddTrigger(trigger, EventTriggerType.PointerExit, () =>
        {
            cell.Bg.color = cell.Lock == null ? SlotColor : LockedSlotColor;
            ShowCountLine();
        });
        return cell;
    }

    static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
    }

    static Text MakeText(string text, Transform parent, Vector2 pos, int size, Color color, float width, float height, TextAnchor anchor)
    {
        var t = GUIManager.Instance.CreateText(text, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos,
            GUIManager.Instance.AveriaSerifBold, size, color, true, Color.black, width, height, false).GetComponent<Text>();
        t.alignment = anchor;
        t.supportRichText = true;
        return t;
    }

    static Image MakeImage(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    static Button MakeButton(Transform parent, string label, Vector2 pos, float width, float height, UnityAction onClick, int fontSize = 14)
    {
        var go = GUIManager.Instance.CreateButton(label, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, width, height);
        var button = DrakeButtonSfx.SoftenButton(go);
        var text = go.GetComponentInChildren<Text>(true);
        if (text)
        {
            text.resizeTextForBestFit = false;
            text.fontSize = fontSize;
        }
        button.onClick.AddListener(onClick);
        return button;
    }

    sealed class Cell
    {
        public GameObject Go = null!;
        public Image Bg = null!;
        public Image Icon = null!;
        public Outline Outline = null!;
        public IconCatalogEntry? Entry;
        public string? Lock;
    }
}
