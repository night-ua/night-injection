using Microsoft.UI.Xaml;
using NightInjection.Core.Models;

namespace NightInjection.UI.Services;

public sealed class ThemeService : IThemeService
{
    public void Apply(AppTheme theme)
    {
        if (App.Window?.Content is not FrameworkElement root)
        {
            return;
        }

        root.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }
}
