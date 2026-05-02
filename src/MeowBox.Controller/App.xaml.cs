using System.Diagnostics;
using Microsoft.UI.Xaml;
using MeowBox.Controller.Services;
using MeowBox.Core.Models;
using MeowBox.Core.Services;
using Microsoft.Windows.Globalization;
using Microsoft.Windows.AppLifecycle;

namespace MeowBox.Controller;

public partial class App : Application
{
    private const string SingleInstanceKey = "MeowBox.Controller";
    private static bool _pendingWindowActivation;
    private static AppInstance? _currentInstance;

    public static MainWindow? MainWindow { get; private set; }

    public static ThemeService ThemeService { get; } = new();

    public static MeowBoxController Controller { get; private set; } = null!;

    public App()
    {
        ApplyStoredLanguagePreference();
        Controller = new MeowBoxController();
        InitializeComponent();
    }

    public static void Restart()
    {
        try
        {
            _ = AppInstance.Restart(string.Empty);
            return;
        }
        catch
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                Current.Exit();
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            });

            Current.Exit();
        }
    }

    public static void RequestWindowActivation()
    {
        if (MainWindow is null)
        {
            _pendingWindowActivation = true;
            return;
        }

        if (!MainWindow.DispatcherQueue.TryEnqueue(static () => MainWindow?.PresentToFront()))
        {
            MainWindow.PresentToFront();
        }
    }

    private static void ApplyStoredLanguagePreference()
    {
        try
        {
            var storedPreference = new AppConfigService().GetStoredLanguagePreference();
            ApplyLanguagePreference(storedPreference);
        }
        catch
        {
            ApplyLanguagePreference(null);
        }
    }

    private static void ApplyLanguagePreference(string? storedPreference)
    {
        var languageTag = AppLanguageService.ResolveEffectiveLanguageTag(storedPreference);
        AppLanguageService.Apply(storedPreference);
        ApplicationLanguages.PrimaryLanguageOverride = languageTag;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (UnelevatedProcessLauncher.IsCurrentProcessElevated())
        {
            var executablePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executablePath) &&
                UnelevatedProcessLauncher.TryStart(
                    executablePath,
                    Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
                    out _))
            {
                Environment.Exit(0);
                return;
            }
        }

        if (!RegisterSingleInstance())
        {
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.PresentToFront();
        MainWindow.DispatcherQueue.TryEnqueue(static () =>
        {
            if (App.MainWindow is null)
            {
                return;
            }

            App.Controller.Initialize(App.MainWindow);
            App.MainWindow.PresentToFront();
            if (_pendingWindowActivation)
            {
                _pendingWindowActivation = false;
                App.MainWindow.PresentToFront();
            }
        });
    }

    private static bool RegisterSingleInstance()
    {
        var keyInstance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);
        if (!keyInstance.IsCurrent)
        {
            var activationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
            _ = RedirectActivationAndExitAsync(keyInstance, activationArguments);
            return false;
        }

        if (!ReferenceEquals(_currentInstance, keyInstance))
        {
            if (_currentInstance is not null)
            {
                _currentInstance.Activated -= OnActivated;
            }

            _currentInstance = keyInstance;
            _currentInstance.Activated += OnActivated;
        }

        return true;
    }

    private static async Task RedirectActivationAndExitAsync(AppInstance keyInstance, AppActivationArguments? activationArguments)
    {
        try
        {
            if (activationArguments is not null)
            {
                await keyInstance.RedirectActivationToAsync(activationArguments);
            }
        }
        finally
        {
            Current.Exit();
        }
    }

    private static void OnActivated(object? sender, AppActivationArguments args)
    {
        RequestWindowActivation();
    }
}
