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
            var storedPreference = new AppConfigService().GetStoredLanguagePreference();
            AppLanguageService.Apply(storedPreference);
        }
        catch
        {
            AppLanguageService.Apply(null);
        }
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