using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;
using NightInjection.UI.Messages;
using NightInjection.UI.Services;
using NightInjection.UI.Views;
using Windows.Graphics;

namespace NightInjection.UI;

public sealed partial class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly ISettingsService _settings;
    private readonly IThemeService _themeService;
    private readonly ILogger<MainWindow> _logger;
    private string _currentSection = "Dashboard";
    private string? _requestedSection;
    private bool _closing;

    public MainWindow(
        IServiceProvider services,
        ISettingsService settings,
        IThemeService themeService,
        ILogger<MainWindow> logger)
    {
        _services = services;
        _settings = settings;
        _themeService = themeService;
        _logger = logger;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Title = "Night Injection";
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }
        RestoreWindow();
        Activated += OnActivated;
        AppWindow.Closing += OnClosing;
        Navigation.Loaded += OnNavigationLoaded;
        WeakReferenceMessenger.Default.Register<MainWindow, NavigationRequestMessage>(
            this,
            static (recipient, message) => recipient.Navigate(message.Value));
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args) =>
        _themeService.Apply(_settings.Current.Theme);

    private void OnNavigationLoaded(object sender, RoutedEventArgs e)
    {
        var requestedSection = Environment.GetEnvironmentVariable("NIGHT_INJECTION_START_SECTION");
        var section = IsKnownSection(_requestedSection ?? string.Empty)
            ? _requestedSection!
            : IsKnownSection(requestedSection ?? string.Empty)
            ? requestedSection!
            : IsKnownSection(_settings.Current.LastSection)
            ? _settings.Current.LastSection
            : "Dashboard";
        Navigate(section);
    }

    public void RequestSection(string section)
    {
        if (!IsKnownSection(section))
        {
            return;
        }

        _requestedSection = section;
        if (Navigation.IsLoaded)
        {
            Navigate(section);
        }
    }

    private void OnNavigationSelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            Navigate("Settings");
            return;
        }

        if (args.SelectedItemContainer?.Tag is string tag)
        {
            Navigate(tag);
        }
    }

    private async void Navigate(string section)
    {
        if (!IsKnownSection(section))
        {
            return;
        }

        _currentSection = section;
        PageHeader.Text = section;
        PageFrame.Content = section switch
        {
            "Dashboard" => _services.GetRequiredService<DashboardPage>(),
            "Inject" => _services.GetRequiredService<InjectPage>(),
            "Library" => _services.GetRequiredService<LibraryPage>(),
            "History" => _services.GetRequiredService<HistoryPage>(),
            "Logs" => _services.GetRequiredService<LogsPage>(),
            "Settings" => _services.GetRequiredService<SettingsPage>(),
            _ => _services.GetRequiredService<DashboardPage>()
        };

        if (section == "Settings")
        {
            if (Navigation.SettingsItem is not null
                && !ReferenceEquals(Navigation.SelectedItem, Navigation.SettingsItem))
            {
                Navigation.SelectedItem = Navigation.SettingsItem;
            }
        }
        else
        {
            var item = Navigation.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, section, StringComparison.Ordinal));
            if (item is not null && !ReferenceEquals(Navigation.SelectedItem, item))
            {
                Navigation.SelectedItem = item;
            }
        }

        if (_settings.Current.LastSection != section)
        {
            await _settings.SaveAsync(_settings.Current with { LastSection = section });
        }
    }

    private void RestoreWindow()
    {
        var current = _settings.Current;
        if (!current.RememberWindowSize)
        {
            AppWindow.Resize(new SizeInt32(
                AppSettings.DefaultWindowWidth,
                AppSettings.DefaultWindowHeight));
            return;
        }

        AppWindow.Resize(new SizeInt32(
            Math.Clamp(current.WindowWidth, 900, 3840),
            Math.Clamp(current.WindowHeight, 620, 2160)));
        if (current.WindowMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closing)
        {
            return;
        }

        args.Cancel = true;
        _closing = true;
        try
        {
            var maximized = sender.Presenter is OverlappedPresenter presenter
                && presenter.State == OverlappedPresenterState.Maximized;
            var current = _settings.Current with { LastSection = _currentSection };
            if (current.RememberWindowSize)
            {
                current = current with
                {
                    WindowWidth = sender.Size.Width,
                    WindowHeight = sender.Size.Height,
                    WindowMaximized = maximized,
                    WindowGeometry = $"{sender.Size.Width}x{sender.Size.Height}"
                };
            }

            await _settings.SaveAsync(current);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not save window state during shutdown.");
        }
        finally
        {
            Activated -= OnActivated;
            sender.Closing -= OnClosing;
            WeakReferenceMessenger.Default.UnregisterAll(this);
            Application.Current.Exit();
        }
    }

    private static bool IsKnownSection(string section) =>
        section is "Dashboard" or "Inject" or "Library"
            or "History" or "Logs" or "Settings";
}
