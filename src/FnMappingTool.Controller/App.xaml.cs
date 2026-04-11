using System.Diagnostics;
using Microsoft.UI.Xaml;
using FnMappingTool.Controller.Services;
using FnMappingTool.Core.Services;

namespace FnMappingTool.Controller;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public static ThemeService ThemeService { get; } = new();

    public static FnMappingToolController Controller { get; } = new();

    public App()
    {
        ApplyStoredLanguagePreference();
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
            var configuration = new AppConfigService().Load();
            AppLanguageService.Apply(configuration.Preferences.Language);
        }
        catch
        {
            AppLanguageService.Apply(null);
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        Controller.Initialize(MainWindow);
        MainWindow.PresentToFront();
        MainWindow.DispatcherQueue.TryEnqueue(static () =>
        {
            App.MainWindow?.PresentToFront();
        });
    }
}