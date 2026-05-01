using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using MeowBox.Core.Models;
using MeowBox.Core.Services;

namespace MeowBox.Worker.Services;

internal sealed class WorkerOsdService : IDisposable
{
    private readonly OsdForm _form = new();

    public void Show(string title, IconConfiguration icon, OsdPreferences preferences, string themePreference)
    {
        _form.ShowOsd(title, icon, preferences, ResolveDarkTheme(themePreference));
    }

    public void Dispose()
    {
        _form.Dispose();
    }

    private static bool ResolveDarkTheme(string? themePreference)
    {
        return themePreference switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => IsSystemDarkTheme()
        };
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intValue && intValue == 0;
        }
        catch
        {
            return true;
        }
    }
}

internal sealed class OsdForm : Form
{
    private const float SizeBaselineFactor = 0.7f;
    private const int BaseIconOnlySizePx = 220;
    private const int BaseTextOnlyWidthPx = 360;
    private const int BaseTextOnlyHeightPx = 96;
    private const int BaseBothWidthPx = 320;
    private const int BaseBothHeightPx = 194;
    private const int BaseIconSizePx = 92;
    private const int BaseTitleFontPx = 26;
    private const int BaseHorizontalPaddingPx = 20;
    private const int BaseTopPaddingPx = 18;
    private const int BaseBottomPaddingPx = 18;
    private const int BaseItemSpacingPx = 8;
    private const int BaseBottomMarginPx = 54;
    private const int CornerRadius = 28;
    private const int DwMwWindowCornerPreference = 33;
    private const int DwMwSystemBackdropType = 38;
    private const int DwmwcpRound = 2;
    private const int DwmsbtTransientWindow = 3;
    private const int WcaAccentPolicy = 19;
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExTopmost = 0x00000008;
    private const int SwShowNoActivate = 4;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const int ShowAnimationDurationMs = 180;
    private const int HideAnimationDurationMs = 150;
    private static readonly IntPtr HwndTopMost = new(-1);
    private readonly System.Windows.Forms.Timer _displayTimer;
    private readonly System.Windows.Forms.Timer _animationTimer;

    private bool _darkTheme = true;
    private AnimationPhase _animationPhase = AnimationPhase.Hidden;
    private DateTime _animationStartedAtUtc;
    private int _animationDurationMs;
    private int _animationStartTop;
    private int _animationTargetTop;
    private double _animationStartOpacity;
    private double _animationTargetOpacity;
    private int _steadyLeft;
    private int _steadyTop;
    private int _showOffsetPx = 18;
    private int _hideOffsetPx = 10;
    private int _bottomMarginPx = BaseBottomMarginPx;
    private int _backgroundOpacityPercent = RuntimeDefaults.DefaultOsdBackgroundOpacityPercent;
    private int _scaledHorizontalPaddingPx = BaseHorizontalPaddingPx;
    private int _scaledTopPaddingPx = BaseTopPaddingPx;
    private int _scaledBottomPaddingPx = BaseBottomPaddingPx;
    private int _scaledItemSpacingPx = BaseItemSpacingPx;
    private int _scaledStandardIconSizePx = BaseIconSizePx;
    private int _scaledIconOnlyIconSizePx = 132;
    private string _requestedDisplayMode = OsdDisplayMode.IconAndText;
    private string _titleText = string.Empty;
    private Color _titleColor = Color.White;
    private Bitmap? _iconBitmap;
    private Rectangle _iconBounds = Rectangle.Empty;
    private Rectangle _titleBounds = Rectangle.Empty;
    private Font _titleFont;
    private Color _contentColor = Color.White;

    public OsdForm()
    {
        SuspendLayout();

        _titleFont = CreateTitleFont(BaseTitleFontPx);

        _displayTimer = new System.Windows.Forms.Timer();
        _displayTimer.Tick += (_, _) =>
        {
            _displayTimer.Stop();
            BeginHideAnimation();
        };

        _animationTimer = new System.Windows.Forms.Timer
        {
            Interval = 15
        };
        _animationTimer.Tick += (_, _) => AdvanceAnimation();

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        BackColor = Color.Black;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        ClientSize = new Size(BaseBothWidthPx, BaseBothHeightPx);
        ResumeLayout(false);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate | WsExTopmost;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWindowMaterial();
        ApplyRoundedRegion();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyRoundedRegion();
        LayoutContent();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _iconBitmap?.Dispose();
            _titleFont.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.CompositingQuality = CompositingQuality.HighQuality;

        if (ShouldShowIcon() && _iconBitmap is not null && !_iconBounds.IsEmpty)
        {
            e.Graphics.DrawImage(_iconBitmap, _iconBounds);
        }

        if (ShouldShowText() && !_titleBounds.IsEmpty && !string.IsNullOrWhiteSpace(_titleText))
        {
            DrawTitle(e.Graphics);
        }
    }

    public void ShowOsd(string title, IconConfiguration icon, OsdPreferences preferences, bool darkTheme)
    {
        _darkTheme = darkTheme;
        _backgroundOpacityPercent = Math.Clamp(preferences.BackgroundOpacityPercent, 0, 100);
        _requestedDisplayMode = preferences.DisplayMode;
        _titleText = title ?? string.Empty;
        _contentColor = GetContentColor(darkTheme);
        _titleColor = _contentColor;

        LoadImage(icon, darkTheme);
        EnsureWindowReady();
        var targetWindow = ResolveTargetWindowHandle();
        var targetScreen = ResolveTargetScreen(targetWindow);
        ApplyScaledLayout(ResolveScaleFactor(targetWindow), preferences);
        UpdateSteadyPosition(targetScreen.WorkingArea);
        BeginShowAnimation(Math.Clamp(preferences.DurationMs, 500, 10000));
        Invalidate();
    }

    private void BeginShowAnimation(int durationMs)
    {
        _displayTimer.Stop();
        _animationTimer.Stop();

        var startTop = _steadyTop + _showOffsetPx;
        PresentTopmostWindow(startTop, 0d);

        StartAnimation(AnimationPhase.Showing, startTop, _steadyTop, Opacity, 1d, ShowAnimationDurationMs);
        _displayTimer.Interval = durationMs;
    }

    private void LayoutContent()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        var showIcon = ShouldShowIcon();
        var showText = ShouldShowText();
        _iconBounds = Rectangle.Empty;
        _titleBounds = Rectangle.Empty;

        if (showIcon && showText)
        {
            var iconSize = _scaledStandardIconSizePx;
            var iconLeft = (ClientSize.Width - iconSize) / 2;
            var iconTop = _scaledTopPaddingPx;
            _iconBounds = new Rectangle(iconLeft, iconTop, iconSize, iconSize);

            var titleWidth = Math.Max(120, ClientSize.Width - (_scaledHorizontalPaddingPx * 2));
            var titleHeight = Math.Max(28, ClientSize.Height - _iconBounds.Bottom - _scaledItemSpacingPx - _scaledBottomPaddingPx);
            var titleLeft = (ClientSize.Width - titleWidth) / 2;
            var titleTop = _iconBounds.Bottom + _scaledItemSpacingPx;
            _titleBounds = new Rectangle(titleLeft, titleTop, titleWidth, titleHeight);
            return;
        }

        if (showIcon)
        {
            var iconSize = _scaledIconOnlyIconSizePx;
            var iconLeft = (ClientSize.Width - iconSize) / 2;
            var iconTop = (ClientSize.Height - iconSize) / 2;
            _iconBounds = new Rectangle(iconLeft, iconTop, iconSize, iconSize);
        }

        if (showText)
        {
            var titleWidth = Math.Max(160, ClientSize.Width - (_scaledHorizontalPaddingPx * 2));
            var titleHeight = Math.Max(32, ClientSize.Height - _scaledTopPaddingPx - _scaledBottomPaddingPx);
            var titleLeft = (ClientSize.Width - titleWidth) / 2;
            var titleTop = (ClientSize.Height - titleHeight) / 2;
            _titleBounds = new Rectangle(titleLeft, titleTop, titleWidth, titleHeight);
        }
    }

    private void EnsureWindowReady()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (!IsHandleCreated)
        {
            CreateControl();
            _ = Handle;
        }

        ApplyWindowMaterial();
        ApplyRoundedRegion();
        LayoutContent();
        PerformLayout();
    }

    private void PresentTopmostWindow(int top, double opacity)
    {
        if (!IsHandleCreated)
        {
            return;
        }

        Opacity = Math.Clamp(opacity, 0d, 1d);
        Left = _steadyLeft;
        Top = top;

        if (!Visible)
        {
            Show();
        }

        ShowWindow(Handle, SwShowNoActivate);
        SetWindowPos(
            Handle,
            HwndTopMost,
            _steadyLeft,
            top,
            Width,
            Height,
            SwpNoActivate | SwpShowWindow);
    }

    private void ApplyScaledLayout(float dpiScaleFactor, OsdPreferences preferences)
    {
        var scale = Math.Max(0.42f, dpiScaleFactor * (Math.Clamp(preferences.ScalePercent, 60, 200) / 100f) * SizeBaselineFactor);
        _showOffsetPx = ScaleValue(18, scale);
        _hideOffsetPx = ScaleValue(10, scale);
        _bottomMarginPx = ScaleValue(BaseBottomMarginPx, scale);
        _scaledHorizontalPaddingPx = ScaleValue(BaseHorizontalPaddingPx, scale);
        _scaledTopPaddingPx = ScaleValue(BaseTopPaddingPx, scale);
        _scaledBottomPaddingPx = ScaleValue(BaseBottomPaddingPx, scale);
        _scaledItemSpacingPx = ScaleValue(BaseItemSpacingPx, scale);

        _scaledStandardIconSizePx = ScaleValue(BaseIconSizePx, scale);
        _scaledIconOnlyIconSizePx = Math.Min(
            ScaleValue(132, scale),
            Math.Max(ScaleValue(84, scale), ScaleValue(BaseIconOnlySizePx, scale) - ScaleValue(36, scale)));
        _titleFont.Dispose();
        _titleFont = CreateTitleFont(Math.Max(19, ScaleValue(BaseTitleFontPx, scale)));

        var displayMode = ResolveEffectiveDisplayMode();
        var measuredTitleWidth = Math.Min(
            ScaleValue(520, scale),
            Math.Max(
                ScaleValue(160, scale),
                TextRenderer.MeasureText(_titleText, _titleFont).Width + ScaleValue(12, scale)));

        ClientSize = displayMode switch
        {
            OsdDisplayMode.IconOnly => new Size(ScaleValue(BaseIconOnlySizePx, scale), ScaleValue(BaseIconOnlySizePx, scale)),
            OsdDisplayMode.TextOnly => new Size(
                Math.Max(ScaleValue(BaseTextOnlyWidthPx, scale), measuredTitleWidth + (_scaledHorizontalPaddingPx * 2)),
                ScaleValue(BaseTextOnlyHeightPx, scale)),
            _ => new Size(
                Math.Max(ScaleValue(BaseBothWidthPx, scale), measuredTitleWidth + (_scaledHorizontalPaddingPx * 2)),
                ScaleValue(BaseBothHeightPx, scale))
        };

        ApplyRoundedRegion();
        LayoutContent();
        Invalidate();
    }

    private void UpdateSteadyPosition(Rectangle bounds)
    {
        _steadyLeft = bounds.Left + (bounds.Width - Width) / 2;
        _steadyTop = bounds.Bottom - Height - _bottomMarginPx;
    }

    private void LoadImage(IconConfiguration icon, bool darkTheme)
    {
        _iconBitmap?.Dispose();
        _iconBitmap = null;

        var path = ResolvePreferredIconPath(icon);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _iconBitmap = LoadRequiredBitmap(path, _contentColor, "OSD icon");
    }

    private static string? ResolvePreferredIconPath(IconConfiguration icon)
    {
        var assetKey = Path.GetFileNameWithoutExtension(icon.Path);
        return ResolvePngPath(BuiltInAssetResolver.ResolveOsdAssetPath(assetKey));
    }

    private static string? ResolvePngPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)
            ? path
            : null;
    }

    private static Bitmap LoadRequiredBitmap(string path, Color tintColor, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException($"The {label} file was not found.", path);
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var image = Image.FromStream(stream);
            return TintBitmap(new Bitmap(image), tintColor);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Failed to load {label} from '{path}'.", exception);
        }
    }

    private static Bitmap TintBitmap(Bitmap source, Color color)
    {
        var tinted = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var pixel = source.GetPixel(x, y);
                tinted.SetPixel(x, y, pixel.A == 0 ? Color.Transparent : Color.FromArgb(pixel.A, color));
            }
        }

        source.Dispose();
        return tinted;
    }

    private static Font CreateTitleFont(float pixelSize)
    {
        try
        {
            return new Font("Segoe UI Variable Text", pixelSize, FontStyle.Regular, GraphicsUnit.Pixel);
        }
        catch
        {
            return new Font("Segoe UI", pixelSize, FontStyle.Regular, GraphicsUnit.Pixel);
        }
    }

    private static Color GetContentColor(bool darkTheme)
    {
        return darkTheme
            ? Color.FromArgb(245, 248, 250)
            : Color.FromArgb(48, 52, 60);
    }

    private void ApplyWindowMaterial()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        TrySetBackdropType();
        TryEnableAcrylic();
        TrySetRoundedCornerPreference();
    }

    private void TrySetBackdropType()
    {
        try
        {
            var value = DwmsbtTransientWindow;
            DwmSetWindowAttribute(Handle, DwMwSystemBackdropType, ref value, Marshal.SizeOf<int>());
        }
        catch
        {
        }
    }

    private void TrySetRoundedCornerPreference()
    {
        try
        {
            var preference = DwmwcpRound;
            DwmSetWindowAttribute(Handle, DwMwWindowCornerPreference, ref preference, Marshal.SizeOf<int>());
        }
        catch
        {
        }
    }

    private void TryEnableAcrylic()
    {
        try
        {
            var alpha = (int)Math.Round(255 * (_backgroundOpacityPercent / 100d));
            var accent = new AccentPolicy
            {
                AccentState = AccentEnableAcrylicBlurBehind,
                AccentFlags = 0,
                GradientColor = ToAbgr(_darkTheme
                    ? Color.FromArgb(alpha, 10, 10, 14)
                    : Color.FromArgb(alpha, 245, 245, 248))
            };

            var size = Marshal.SizeOf<AccentPolicy>();
            var accentPtr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WcaAccentPolicy,
                    Data = accentPtr,
                    SizeOfData = size
                };
                SetWindowCompositionAttribute(Handle, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
        catch
        {
        }
    }

    private void ApplyRoundedRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        var scaleFactor = Math.Max(Width / (float)BaseBothWidthPx, Height / (float)BaseBothHeightPx);
        var cornerDiameter = ScaleValue(CornerRadius * 2, scaleFactor);
        var regionHandle = CreateRoundRectRgn(0, 0, Width + 1, Height + 1, cornerDiameter, cornerDiameter);
        Region?.Dispose();
        Region = Region.FromHrgn(regionHandle);
        DeleteObject(regionHandle);
    }

    private void StartAnimation(AnimationPhase phase, int startTop, int targetTop, double startOpacity, double targetOpacity, int durationMs)
    {
        _animationPhase = phase;
        _animationStartedAtUtc = DateTime.UtcNow;
        _animationDurationMs = Math.Max(1, durationMs);
        _animationStartTop = startTop;
        _animationTargetTop = targetTop;
        _animationStartOpacity = startOpacity;
        _animationTargetOpacity = targetOpacity;
        _animationTimer.Start();
        AdvanceAnimation();
    }

    private void BeginHideAnimation()
    {
        if (!Visible || _animationPhase == AnimationPhase.Hiding)
        {
            return;
        }

        StartAnimation(AnimationPhase.Hiding, Top, _steadyTop + _hideOffsetPx, Opacity, 0d, HideAnimationDurationMs);
    }

    private void AdvanceAnimation()
    {
        if (_animationPhase == AnimationPhase.Hidden)
        {
            _animationTimer.Stop();
            return;
        }

        var elapsedMs = (DateTime.UtcNow - _animationStartedAtUtc).TotalMilliseconds;
        var progress = Math.Clamp(elapsedMs / _animationDurationMs, 0d, 1d);
        var easedProgress = _animationPhase == AnimationPhase.Showing ? EaseOutCubic(progress) : EaseInCubic(progress);

        Top = (int)Math.Round(_animationStartTop + ((_animationTargetTop - _animationStartTop) * easedProgress));
        Opacity = _animationStartOpacity + ((_animationTargetOpacity - _animationStartOpacity) * easedProgress);

        if (progress < 1d)
        {
            return;
        }

        switch (_animationPhase)
        {
            case AnimationPhase.Showing:
                _animationPhase = AnimationPhase.Visible;
                _animationTimer.Stop();
                PresentTopmostWindow(_steadyTop, 1d);
                _displayTimer.Start();
                break;
            case AnimationPhase.Hiding:
                _animationPhase = AnimationPhase.Hidden;
                _animationTimer.Stop();
                Hide();
                break;
            default:
                _animationTimer.Stop();
                break;
        }
    }

    private void DrawTitle(Graphics graphics)
    {
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var brush = new SolidBrush(_titleColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisWord
        };
        format.FormatFlags = StringFormatFlags.LineLimit;
        graphics.DrawString(_titleText, _titleFont, brush, _titleBounds, format);
    }

    private string ResolveEffectiveDisplayMode()
    {
        var hasIcon = _iconBitmap is not null;
        var hasText = !string.IsNullOrWhiteSpace(_titleText);

        return _requestedDisplayMode switch
        {
            OsdDisplayMode.IconOnly when hasIcon => OsdDisplayMode.IconOnly,
            OsdDisplayMode.TextOnly when hasText => OsdDisplayMode.TextOnly,
            OsdDisplayMode.IconAndText when hasIcon && hasText => OsdDisplayMode.IconAndText,
            _ when hasIcon && hasText => OsdDisplayMode.IconAndText,
            _ when hasIcon => OsdDisplayMode.IconOnly,
            _ => OsdDisplayMode.TextOnly
        };
    }

    private bool ShouldShowIcon()
    {
        if (_iconBitmap is null)
        {
            return false;
        }

        var displayMode = ResolveEffectiveDisplayMode();
        return displayMode is OsdDisplayMode.IconOnly or OsdDisplayMode.IconAndText;
    }

    private bool ShouldShowText()
    {
        if (string.IsNullOrWhiteSpace(_titleText))
        {
            return false;
        }

        var displayMode = ResolveEffectiveDisplayMode();
        return displayMode is OsdDisplayMode.TextOnly or OsdDisplayMode.IconAndText;
    }

    private static float ResolveScaleFactor(IntPtr targetWindow)
    {
        try
        {
            if (targetWindow != IntPtr.Zero)
            {
                return Math.Max(1f, GetDpiForWindow(targetWindow) / 96f);
            }
        }
        catch
        {
        }

        try
        {
            if (Application.OpenForms.Count > 0 && Application.OpenForms[0] is { } openForm)
            {
                return Math.Max(1f, GetDpiForWindow(openForm.Handle) / 96f);
            }
        }
        catch
        {
        }

        return 1f;
    }

    private static Screen ResolveTargetScreen(IntPtr targetWindow)
    {
        try
        {
            if (targetWindow != IntPtr.Zero)
            {
                return Screen.FromHandle(targetWindow);
            }
        }
        catch
        {
        }

        return Screen.FromPoint(Cursor.Position);
    }

    private IntPtr ResolveTargetWindowHandle()
    {
        try
        {
            var foregroundWindow = GetForegroundWindow();
            return foregroundWindow != Handle ? foregroundWindow : IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static int ScaleValue(int value, float scaleFactor) =>
        (int)Math.Round(value * scaleFactor);

    private static int ToAbgr(Color color)
    {
        return (color.A << 24) | (color.B << 16) | (color.G << 8) | color.R;
    }

    private static double EaseOutCubic(double value)
    {
        var inverse = 1d - value;
        return 1d - (inverse * inverse * inverse);
    }

    private static double EaseInCubic(double value)
    {
        return value * value * value;
    }

    private enum AnimationPhase
    {
        Hidden,
        Showing,
        Visible,
        Hiding
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
}
