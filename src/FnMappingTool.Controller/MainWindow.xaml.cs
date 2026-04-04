using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using FnMappingTool.Controller.Views;
using WinRT.Interop;
using Windows.Graphics;

namespace FnMappingTool.Controller;

public sealed partial class MainWindow : Window
{
    private const int MinimumWidth = 1180;
    private const int MinimumHeight = 820;
    private const int GwlWndProc = -4;
    private const int WmGetMinMaxInfo = 0x0024;

    public Services.FnMappingToolController Controller => App.Controller;

    private readonly Dictionary<string, Type> _pages = new()
    {
        ["keys"] = typeof(KeysPage),
        ["mappings"] = typeof(MappingsPage),
        ["settings"] = typeof(SettingsPage)
    };

    private AppWindow? _appWindow;
    private IntPtr _windowHandle;
    private IntPtr _previousWndProc;
    private WindowProcDelegate? _windowProcDelegate;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureWindowChrome();
        ConfigureNavigation();
        Controller.PropertyChanged += OnControllerPropertyChanged;
        Closed += OnWindowClosed;
    }

    private void ConfigureWindowChrome()
    {
        SystemBackdrop = new MicaBackdrop
        {
            Kind = MicaKind.BaseAlt
        };

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        _windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
        }

        InstallWindowProc();
        _appWindow.Resize(GetMinimumWindowSizePixels());
        UpdateServiceIndicator();
    }

    private void ConfigureNavigation()
    {
        ShellNavigationView.SelectedItem = KeysItem;
        Navigate("keys");
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
            ContentFrame.Navigate(pageType);
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
        if (e.PropertyName == nameof(Services.FnMappingToolController.ServiceRunning))
        {
            DispatcherQueue.TryEnqueue(UpdateServiceIndicator);
        }
    }

    private void UpdateServiceIndicator()
    {
        var core = Controller.ServiceRunning ? ColorHelper.FromArgb(255, 76, 196, 115) : ColorHelper.FromArgb(255, 255, 111, 97);
        var outer = Controller.ServiceRunning ? ColorHelper.FromArgb(214, 76, 196, 115) : ColorHelper.FromArgb(214, 255, 111, 97);
        var glow = Controller.ServiceRunning ? ColorHelper.FromArgb(72, 76, 196, 115) : ColorHelper.FromArgb(72, 255, 111, 97);

        ServiceStatusCoreInner.Fill = new SolidColorBrush(core);
        ServiceStatusCoreOuter.Fill = new SolidColorBrush(outer);
        ServiceStatusGlow.Fill = new SolidColorBrush(glow);

        ServiceStatusToolTip.Content = Controller.ServiceRunning
            ? "Background service running"
            : "Background service stopped";
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        Controller.PropertyChanged -= OnControllerPropertyChanged;
        RestoreWindowProc();
        App.Controller.Dispose();
    }

    private SizeInt32 GetMinimumWindowSizePixels()
    {
        var dpi = GetDpiForWindow(_windowHandle);
        return new SizeInt32(
            MulDiv(MinimumWidth, dpi, 96),
            MulDiv(MinimumHeight, dpi, 96));
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

    [DllImport("user32.dll")]
    private static extern int GetDpiForWindow(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    private static extern int MulDiv(int number, int numerator, int denominator);
}
