using Microsoft.UI.Xaml;
using FnMappingTool.Controller.Services;

namespace FnMappingTool.Controller;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public static ThemeService ThemeService { get; } = new();

    public static FnMappingToolController Controller { get; } = new();

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
        Controller.Initialize(MainWindow);
    }
}
