using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WindowsApiLib.Shell;

namespace ExpControlsLib;

/// <summary>
/// Owns folder navigation history independently from a visual control.
/// Targets are removed from history only after the requested navigation succeeds.
/// </summary>
internal sealed class ExpNavigationHistory
{
    private readonly Stack<CShellItem> _back = new();
    private readonly Stack<CShellItem> _forward = new();
    private readonly Func<CShellItem, CShellItem, bool> _areSame;

    public ExpNavigationHistory(Func<CShellItem, CShellItem, bool>? areSame = null)
    {
        _areSame = areSame ?? ((left, right) => ReferenceEquals(left, right));
    }

    public CShellItem? Current { get; private set; }
    public bool IsNavigating { get; private set; }
    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public bool CanGoUp => Current?.Parent is not null;

    public void RecordSelection(CShellItem item)
    {
        if (!IsNavigating && Current is not null && !_areSame(Current, item))
        {
            _back.Push(Current);
            _forward.Clear();
        }

        Current = item;
    }

    public async Task<bool> GoBackAsync(Func<CShellItem, Task<bool>> navigate)
    {
        ArgumentNullException.ThrowIfNull(navigate);
        if (_back.Count == 0) return false;

        var current = Current;
        var target = _back.Peek();
        IsNavigating = true;
        try
        {
            if (!await navigate(target)) return false;
            _back.Pop();
            if (current is not null) _forward.Push(current);
            return true;
        }
        finally
        {
            IsNavigating = false;
        }
    }

    public async Task<bool> GoForwardAsync(Func<CShellItem, Task<bool>> navigate)
    {
        ArgumentNullException.ThrowIfNull(navigate);
        if (_forward.Count == 0) return false;

        var current = Current;
        var target = _forward.Peek();
        IsNavigating = true;
        try
        {
            if (!await navigate(target)) return false;
            _forward.Pop();
            if (current is not null) _back.Push(current);
            return true;
        }
        finally
        {
            IsNavigating = false;
        }
    }
}
