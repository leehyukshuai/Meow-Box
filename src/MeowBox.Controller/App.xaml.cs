using System.Diagnostics;
using Microsoft.UI.Xaml;
using MeowBox.Controller.Services;
using MeowBox.Core.Models;
using MeowBox.Core.Services;
using Microsoft.Windows.Globalization;

namespace MeowBox.Controller;

public partial class App : Application
{
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
        AppLanguageService.Apply(storedPreference);

        var normalizedPreference = AppLanguageService.ResolveStoredPreference(storedPreference);
        ApplicationLanguages.PrimaryLanguageOverride = string.Equals(
            normalizedPreference,
            AppLanguagePreference.System,
            StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : AppLanguageService.ResolveEffectiveLanguageTag(normalizedPreference);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
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
        });
    }
}
