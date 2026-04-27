using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MeowBox.Controller.Services;
using MeowBox.Controller.Views;
using MeowBox.Core.Services;
using WinRT.Interop;
using Windows.Graphics;

namespace MeowBox.Controller;

public sealed partial class MainWindow : Window
{
    private const int InitialWidth = 1080;
    private const int InitialHeight = 760;
    private const int MinimumWidth = InitialWidth;
    private const int MinimumHeight = InitialHeight;
    private const int GwlWndProc = -4;
    private const int WmGetMinMaxInfo = 0x0024;
    private const int SwRestore = 9;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private static readonly IntPtr HwndTop = IntPtr.Zero;

    public Services.MeowBoxController Controller => App.Controller;

    private readonly Dictionary<string, Type> _pages = new()
    {
        ["keyboard"] = typeof(MappingsPage),
        ["touchpad"] = typeof(TouchpadPage),
        ["battery"] = typeof(BatteryPage),
        ["settings"] = typeof(SettingsPage)
    };

    private readonly Dictionary<string, string> _pageTitleKeys = new()
    {
        ["keyboard"] = "PageTitle.Mappings",
        ["touchpad"] = "PageTitle.Touchpad",
        ["battery"] = "PageTitle.Battery",
        ["settings"] = "PageTitle.Settings"
    };

    private AppWindow? _appWindow;
    private IntPtr _windowHandle;
    private IntPtr _previousWndProc;
    private WindowProcDelegate? _windowProcDelegate;

    public MainWindow()
    {
        InitializeComponent();
        ApplyLocalizedShellText();
        ConfigureWindowChrome();
        ConfigureNavigation();
        Controller.PropertyChanged += OnControllerPropertyChanged;
        Closed += OnWindowClosed;
    }

    public void PresentToFront()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            Activate();
            ShowWindow(_windowHandle, SwRestore);
            _ = SetWindowPos(_windowHandle, HwndTop, 0, 0, 0, 0, SwpNoMove | SwpNoSize);
            _ = SetForegroundWindow(_windowHandle);
            _ = SetFocus(_windowHandle);
        }
        catch
        {
        }
    }

    private void ApplyLocalizedShellText()
    {
        var appTitle = Localizer.GetString("App.Title");
        Title = appTitle;
        AppTitleTextBlock.Text = appTitle;
        MappingsItem.Content = Localizer.GetString("Navigation.Mappings");
        TouchpadItem.Content = Localizer.GetString("Navigation.Touchpad");
        BatteryItem.Content = Localizer.GetString("Navigation.Battery");
        SettingsItem.Content = Localizer.GetString("Navigation.Settings");
    }

    private void ConfigureWindowChrome()
    {
        _windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
        }

        var applicationIconPath = BuiltInAssetResolver.ResolveApplicationIconPath("app");
        if (!string.IsNullOrWhiteSpace(applicationIconPath))
        {
            _appWindow.SetIcon(applicationIconPath);
        }

        InstallWindowProc();
        _appWindow.Resize(GetWindowSizePixels(InitialWidth, InitialHeight));
        UpdateServiceIndicator();
        UpdateQuickServiceButton();
    }

    private void ConfigureNavigation()
    {
        ShellNavigationView.SelectedItem = MappingsItem;
        Navigate("keyboard");
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            Navigate("settings");
            return;
        }

        if (args.SelectedItemContainer?.Tag is string key)
        {
            Navigate(key);
        }
    }

    private void Navigate(string key)
    {
        if (_pages.TryGetValue(key, out var pageType))
        {
            Controller.SetTouchpadMonitoringActive(string.Equals(key, "touchpad", StringComparison.OrdinalIgnoreCase));
            ContentFrame.Navigate(pageType);
            UpdatePageChrome(key);
        }
    }

    private void InstallWindowProc()
    {
        _windowProcDelegate = WindowProc;
        var functionPointer = Marshal.GetFunctionPointerForDelegate(_windowProcDelegate);
        _previousWndProc = SetWindowLongPtr(_windowHandle, GwlWndProc, functionPointer);
    }

    private void RestoreWindowProc()
    {
        if (_previousWndProc != IntPtr.Zero && _windowHandle != IntPtr.Zero)
        {
            SetWindowLongPtr(_windowHandle, GwlWndProc, _previousWndProc);
            _previousWndProc = IntPtr.Zero;
        }
    }

    private IntPtr WindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmGetMinMaxInfo)
        {
            var minMaxInfo = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var dpi = GetDpiForWindow(hWnd);
            minMaxInfo.ptMinTrackSize.x = MulDiv(MinimumWidth, dpi, 96);
            minMaxInfo.ptMinTrackSize.y = MulDiv(MinimumHeight, dpi, 96);
            Marshal.StructureToPtr(minMaxInfo, lParam, true);
            return IntPtr.Zero;
        }

        return CallWindowProc(_previousWndProc, hWnd, message, wParam, lParam);
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Services.MeowBoxController.ServiceState) or nameof(Services.MeowBoxController.ServiceRunning))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateServiceIndicator();
                UpdateQuickServiceButton();
            });
        }
    }

    private void UpdateServiceIndicator()
    {
        ServiceStatusTextBlock.Text = Controller.ServiceState switch
        {
            WorkerServiceState.Running => Localizer.GetString("ServiceStatus.Running"),
            WorkerServiceState.Starting => Localizer.GetString("ServiceStatus.Starting"),
            WorkerServiceState.Stopping => Localizer.GetString("ServiceStatus.Stopping"),
            WorkerServiceState.UnexpectedlyStopped => Localizer.GetString("ServiceStatus.WorkerStopped"),
            _ => Localizer.GetString("ServiceStatus.Stopped")
        };

        var glowColor = Controller.ServiceState switch
        {
            WorkerServiceState.Running => ColorHelper.FromArgb(96, 76, 196, 115),
            WorkerServiceState.Starting or WorkerServiceState.Stopping => ColorHelper.FromArgb(96, 255, 192, 82),
            _ => ColorHelper.FromArgb(96, 255, 111, 97)
        };

        var haloColor = Controller.ServiceState switch
        {
            WorkerServiceState.Running => ColorHelper.FromArgb(255, 58, 128, 78),
            WorkerServiceState.Starting or WorkerServiceState.Stopping => ColorHelper.FromArgb(255, 191, 132, 42),
            _ => ColorHelper.FromArgb(255, 144, 54, 48)
        };

        var coreColor = Controller.ServiceState switch
        {
            WorkerServiceState.Running => ColorHelper.FromArgb(255, 118, 232, 145),
            WorkerServiceState.Starting or WorkerServiceState.Stopping => ColorHelper.FromArgb(255, 255, 214, 120),
            _ => ColorHelper.FromArgb(255, 255, 138, 124)
        };

        ServiceStatusGlow.Fill = new SolidColorBrush(glowColor);
        ServiceStatusHalo.Fill = new SolidColorBrush(haloColor);
        ServiceStatusCore.Fill = new SolidColorBrush(coreColor);
    }

    private void UpdateQuickServiceButton()
    {
        QuickServiceButtonTextBlock.Text = Controller.ServiceState switch
        {
            WorkerServiceState.Running => Localizer.GetString("QuickService.Stop"),
            WorkerServiceState.Starting => Localizer.GetString("QuickService.Starting"),
            WorkerServiceState.Stopping => Localizer.GetString("QuickService.Stopping"),
            _ => Localizer.GetString("QuickService.Start")
        };
        QuickServiceButton.IsEnabled = Controller.ServiceState is not WorkerServiceState.Starting and not WorkerServiceState.Stopping;
    }

    private void UpdatePageChrome(string key)
    {
        PageContextTextBlock.Text = _pageTitleKeys.TryGetValue(key, out var titleKey)
            ? Localizer.GetString(titleKey)
            : Localizer.GetString("App.Title");
    }

    private async void OnQuickServiceButtonClick(object sender, RoutedEventArgs e)
    {
        if (Controller.ServiceState is WorkerServiceState.Starting or WorkerServiceState.Stopping)
        {
            return;
        }

        QuickServiceButton.IsEnabled = false;
        try
        {
            if (Controller.ServiceRunning)
            {
                await Controller.StopWorkerServiceAsync();
            }
            else
            {
                await Controller.StartWorkerServiceAsync();
            }
        }
        finally
        {
            UpdateServiceIndicator();
            UpdateQuickServiceButton();
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        Controller.PropertyChanged -= OnControllerPropertyChanged;
        RestoreWindowProc();
        App.Controller.Dispose();
    }

    private SizeInt32 GetWindowSizePixels(int width, int height)
    {
        var dpi = GetDpiForWindow(_windowHandle);
        return new SizeInt32(
            MulDiv(width, dpi, 96),
            MulDiv(height, dpi, 96));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    private delegate IntPtr WindowProcDelegate(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newProc);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallWindowProc(IntPtr prevWndFunc, IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetDpiForWindow(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    private static extern int MulDiv(int number, int numerator, int denominator);
}
