using Microsoft.JSInterop;
using Plantitask.Web.Interfaces;

namespace Plantitask.Web.Services;

/// <summary>
/// The one owner of the current theme. The stylesheets read data-theme on the html element and
/// MudBlazor reads IsDarkMode on its theme provider, and nothing else keeps those two in step, so
/// every switch goes through here and both halves always flip together.
/// </summary>
public class ThemeService : IThemeService
{
    private readonly IJSRuntime _js;

    public ThemeService(IJSRuntime js) => _js = js;

    public bool IsDark { get; private set; }

    public event Action? OnChanged;

    /// <summary>
    /// Picks up the theme the boot script in index.html applied before first paint. Synchronous
    /// on purpose: an awaited read would let MudBlazor render its first frame in the light palette
    /// under a page the stylesheets have already made dark.
    /// </summary>
    public void Initialize()
    {
        if (_js is IJSInProcessRuntime inProcess)
            IsDark = inProcess.Invoke<string>("themeManager.get") == "dark";
    }

    public async Task SetAsync(bool dark)
    {
        if (dark == IsDark)
            return;

        IsDark = dark;
        await _js.InvokeVoidAsync("themeManager.set", dark ? "dark" : "light");
        OnChanged?.Invoke();
    }

    public Task ToggleAsync() => SetAsync(!IsDark);
}
