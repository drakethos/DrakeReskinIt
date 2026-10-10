using System;
using DrakeModsLibs.UI.Toolkit;

namespace DrakesReskinIt.UI;

/// <summary>
/// The look picker the plugin talks to. Draws in the suite's new look (<see cref="ToolkitLookPicker"/>) when that is on
/// (<c>UseToolkitUi</c> in the Libs config), otherwise the classic wood panel (<see cref="ClassicLookPickerPanel"/>).
/// If the new look can't be built the classic one takes over, so Reskin never stops working.
/// </summary>
internal sealed class LookPickerPanel
{
    readonly ClassicLookPickerPanel _classic = new ClassicLookPickerPanel();
    ToolkitLookPicker? _toolkit;
    bool _usingToolkit;

    public bool IsOpen => _usingToolkit ? _toolkit != null && _toolkit.IsOpen : _classic.IsOpen;

    public void Open(ItemDrop.ItemData item, Action? onClosed)
    {
        if (DrakeUiMode.Toolkit)
        {
            try
            {
                _toolkit ??= new ToolkitLookPicker();
                _classic.CloseSilent();
                _usingToolkit = true;
                _toolkit.Open(item, onClosed);
                return;
            }
            catch (Exception ex)
            {
                DrakeUiMode.ReportFailure(ex);
                _usingToolkit = false;
            }
        }

        _toolkit?.CloseSilent();
        _usingToolkit = false;
        _classic.Open(item, onClosed);
    }

    /// <summary>User closed (button / Escape / inventory closed): fires onClosed so the tab host releases.</summary>
    public void Close()
    {
        if (_usingToolkit)
            _toolkit?.Close();
        else
            _classic.Close();
    }

    /// <summary>Tab host switched away: hide without notifying.</summary>
    public void CloseSilent()
    {
        _toolkit?.CloseSilent();
        _classic.CloseSilent();
    }

    /// <summary>Per-frame guard from the plugin.</summary>
    public void Tick()
    {
        if (_usingToolkit)
            _toolkit?.Tick();
        else
            _classic.Tick();
    }
}
