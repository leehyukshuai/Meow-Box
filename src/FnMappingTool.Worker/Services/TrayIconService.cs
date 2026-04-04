using System.Runtime.InteropServices;
using FnMappingTool.Core.Services;

namespace FnMappingTool.Worker.Services;

internal sealed class TrayIconService : IDisposable
{
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmLeftAlign = 0x0000;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const uint WmNull = 0x0000;
    private const int OpenControllerCommandId = 1001;
    private const int ExitServiceCommandId = 1002;

    private readonly Action _openControllerCallback;
    private readonly Action _exitServiceCallback;
    private readonly PopupMenuWindow _popupMenuWindow = new();
    private readonly NotifyIcon _notifyIcon = new()
    {
        Text = "Fn Mapping Tool"
    };

    private Icon? _currentIcon;

    public TrayIconService(Action openControllerCallback, Action exitServiceCallback)
    {
        _openControllerCallback = openControllerCallback;
        _exitServiceCallback = exitServiceCallback;
        _notifyIcon.DoubleClick += (_, _) => _openControllerCallback();
        _notifyIcon.MouseUp += OnNotifyIconMouseUp;
        _notifyIcon.Icon = LoadApplicationIcon();
        _currentIcon = _notifyIcon.Icon;
    }

    public bool IsVisible => _notifyIcon.Visible;

    public void SetVisible(bool visible)
    {
        if (_notifyIcon.Visible == visible)
        {
            return;
        }

        if (visible && _notifyIcon.Icon is null)
        {
            _notifyIcon.Icon = LoadApplicationIcon();
            _currentIcon = _notifyIcon.Icon;
        }

        _notifyIcon.Visible = visible;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.MouseUp -= OnNotifyIconMouseUp;
        _notifyIcon.Dispose();
        _popupMenuWindow.Dispose();
        _currentIcon?.Dispose();
    }

    private void OnNotifyIconMouseUp(object? sender, MouseEventArgs eventArgs)
    {
        if (!_notifyIcon.Visible || eventArgs.Button != MouseButtons.Right)
        {
            return;
        }

        ShowNativeContextMenu();
    }

    private void ShowNativeContextMenu()
    {
        var menuHandle = CreatePopupMenu();
        if (menuHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _ = AppendMenu(menuHandle, MfString, OpenControllerCommandId, "Open Controller");
            _ = AppendMenu(menuHandle, MfSeparator, 0, null);
            _ = AppendMenu(menuHandle, MfString, ExitServiceCommandId, "Exit Service");

            var ownerHandle = _popupMenuWindow.Handle;
            _ = SetForegroundWindow(ownerHandle);

            var cursorPosition = Cursor.Position;
            var selectedCommand = TrackPopupMenuEx(
                menuHandle,
                TpmLeftAlign | TpmBottomAlign | TpmRightButton | TpmReturnCommand,
                cursorPosition.X,
                cursorPosition.Y,
                ownerHandle,
                IntPtr.Zero);

            switch (selectedCommand)
            {
                case OpenControllerCommandId:
                    _openControllerCallback();
                    break;
                case ExitServiceCommandId:
                    _exitServiceCallback();
                    break;
            }

            _ = PostMessage(ownerHandle, WmNull, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            _ = DestroyMenu(menuHandle);
        }
    }

    private static Icon LoadApplicationIcon()
    {
        var iconPath = BuiltInAssetResolver.ResolveApplicationIconPath("app");
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            throw new FileNotFoundException(
                $"Application icon 'app' was not found under assets\\{BuiltInAssetResolver.AppIconsDirectoryName}.");
        }

        try
        {
            return new Icon(iconPath);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Failed to load application icon from '{iconPath}'.", exception);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, nuint uIdNewItem, string? lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int TrackPopupMenuEx(
        IntPtr hMenu,
        uint uFlags,
        int x,
        int y,
        IntPtr hWnd,
        IntPtr lptpm);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private sealed class PopupMenuWindow : NativeWindow, IDisposable
    {
        public PopupMenuWindow()
        {
            CreateHandle(new CreateParams());
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
        }
    }
}
