using System.Threading.Tasks;

namespace ExpControlsLib;

public partial class ExpTree
{
    #region Navigation API

    /// <summary>Navigates back to the previous successfully displayed folder.</summary>
    public void GoBack() => _ = GoBackAsync();

    /// <summary>Asynchronously navigates back to the previous successfully displayed folder.</summary>
    public Task GoBackAsync() => _navigation.GoBackAsync(item => ExpandANodeBaseAsync(item, true));

    /// <summary>Navigates forward to the next successfully displayed folder.</summary>
    public void GoForward() => _ = GoForwardAsync();

    /// <summary>Asynchronously navigates forward to the next successfully displayed folder.</summary>
    public Task GoForwardAsync() => _navigation.GoForwardAsync(item => ExpandANodeBaseAsync(item, true));

    /// <summary>Navigates to the parent folder of the current selection, when available.</summary>
    public void GoUp() => _ = GoUpAsync();

    /// <summary>Asynchronously navigates to the parent folder of the current selection.</summary>
    public async Task GoUpAsync()
    {
        if (_navigation.Current?.Parent is { } parent)
        {
            await ExpandANodeBaseAsync(parent, true);
        }
    }

    /// <summary>Gets whether a previous successfully displayed folder is available.</summary>
    public bool CanGoBack => _navigation.CanGoBack;

    /// <summary>Gets whether a forward navigation target is available.</summary>
    public bool CanGoForward => _navigation.CanGoForward;

    /// <summary>Gets whether the current selection has a parent folder.</summary>
    public bool CanGoUp => _navigation.CanGoUp;

    #endregion
}
