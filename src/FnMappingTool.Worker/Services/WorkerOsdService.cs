using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using FnMappingTool.Core.Models;
using FnMappingTool.Core.Services;
using Svg;

namespace FnMappingTool.Worker.Services;

internal sealed class WorkerOsdService : IDisposable
{
    private readonly OsdForm _form = new();

    public void Show(string title, string? message, IconConfiguration icon, int durationMs)
    {
        _form.ShowOsd(title, message, icon, durationMs, IsDarkTheme());
    }

    public void Dispose()
    {
        _form.Dispose();
    }

    private static bool IsDarkTheme()
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
    private const int BaseClientSizePx = 220;
    private const int BaseIconSizePx = 92;
    private const int BasePaddingLeftPx = 32;
    private const int BasePaddingTopPx = 24;
    private const int BasePaddingRightPx = 32;
    private const int BasePaddingBottomPx = 26;
    private const int BaseBottomMarginPx = 54;
    private const int CornerRadius = 40;
    private const int DwMwWindowCornerPreference = 33;
    private const int DwMwSystemBackdropType = 38;
    private const int DwmwcpRound = 2;
    private const int DwmsbtTransientWindow = 3;
    private const int WcaAccentPolicy = 19;
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const int WsExNoActivate = 0x08000000;
    private const int ShowAnimationDurationMs = 180;
    private const int HideAnimationDurationMs = 150;
    private const int BuiltInImageSize = 160;

    private readonly PictureBox _pictureBox;
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
    private double _steadyOpacity = 0.9d;

    public OsdForm()
    {
        SuspendLayout();

        _pictureBox = new PictureBox
        {
            Size = new Size(92, 92),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };

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
        Padding = new Padding(BasePaddingLeftPx, BasePaddingTopPx, BasePaddingRightPx, BasePaddingBottomPx);
        DoubleBuffered = true;
        BackColor = Color.Black;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        Controls.Add(_pictureBox);
        ClientSize = new Size(BaseClientSizePx, BaseClientSizePx);
        ResumeLayout(false);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate;
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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
        DrawIconHalo(e.Graphics);
    }

    public void ShowOsd(string title, string? message, IconConfiguration icon, int durationMs, bool darkTheme)
    {
        _darkTheme = darkTheme;
        _steadyOpacity = darkTheme ? 0.89d : 0.92d;

        LoadImage(icon, darkTheme);
        EnsureWindowReady();
        var targetWindow = ResolveTargetWindowHandle();
        var targetScreen = ResolveTargetScreen(targetWindow);
        ApplyScaledLayout(ResolveScaleFactor(targetWindow));
        UpdateSteadyPosition(targetScreen.WorkingArea);
        BeginShowAnimation(Math.Max(500, durationMs));
    }

    private void BeginShowAnimation(int durationMs)
    {
        _displayTimer.Stop();

        if (!Visible)
        {
            Opacity = 0;
            Left = _steadyLeft;
            Top = _steadyTop + _showOffsetPx;
            Show();
            Left = _steadyLeft;
            Top = _steadyTop + _showOffsetPx;
        }
        else
        {
            Left = _steadyLeft;
            Top = _steadyTop;
        }

        BringToFront();
        StartAnimation(AnimationPhase.Showing, Top, _steadyTop, Opacity, _steadyOpacity, ShowAnimationDurationMs);
        _displayTimer.Interval = durationMs;
    }

    private void LayoutContent()
    {
        if (IsDisposed || Disposing || Controls.Count == 0)
        {
            return;
        }

        _pictureBox.Left = (Width - _pictureBox.Width) / 2;
        _pictureBox.Top = (Height - _pictureBox.Height) / 2;
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

    private void ApplyScaledLayout(float scaleFactor)
    {
        var scale = Math.Max(1f, scaleFactor);
        _showOffsetPx = ScaleValue(18, scale);
        _hideOffsetPx = ScaleValue(10, scale);
        _bottomMarginPx = ScaleValue(BaseBottomMarginPx, scale);

        _pictureBox.Size = new Size(ScaleValue(BaseIconSizePx, scale), ScaleValue(BaseIconSizePx, scale));
        Padding = new Padding(
            ScaleValue(BasePaddingLeftPx, scale),
            ScaleValue(BasePaddingTopPx, scale),
            ScaleValue(BasePaddingRightPx, scale),
            ScaleValue(BasePaddingBottomPx, scale));
        ClientSize = new Size(ScaleValue(BaseClientSizePx, scale), ScaleValue(BaseClientSizePx, scale));
        ApplyRoundedRegion();
        LayoutContent();
    }

    private void UpdateSteadyPosition(Rectangle bounds)
    {
        _steadyLeft = bounds.Left + (bounds.Width - Width) / 2;
        _steadyTop = bounds.Bottom - Height - _bottomMarginPx;
    }

    private void LoadImage(IconConfiguration icon, bool darkTheme)
    {
        _pictureBox.Image?.Dispose();

        if (icon.Mode == IconSourceMode.CustomFile)
        {
            _pictureBox.Image = LoadRequiredBitmap(icon.Path, darkTheme, "custom OSD icon");
            return;
        }

        var builtInPath = BuiltInAssetResolver.ResolveOsdAssetPath(icon.BuiltInAsset ?? BuiltInOsdAsset.FnLock);
        if (string.IsNullOrWhiteSpace(builtInPath))
        {
            throw new FileNotFoundException(
                $"Built-in OSD asset '{icon.BuiltInAsset ?? BuiltInOsdAsset.FnLock}' was not found under assets\\{BuiltInAssetResolver.OsdIconsDirectoryName}.");
        }

        _pictureBox.Image = LoadRequiredBitmap(builtInPath, darkTheme, $"built-in OSD asset '{icon.BuiltInAsset ?? BuiltInOsdAsset.FnLock}'");
    }

    private static Bitmap LoadRequiredBitmap(string? path, bool darkTheme, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException($"The {label} file was not found.", path);
        }

        try
        {
            if (string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase))
            {
                var svgDocument = SvgDocument.Open<SvgDocument>(path);
                return TintBitmap(svgDocument.Draw(BuiltInImageSize, BuiltInImageSize), darkTheme ? Color.White : Color.Black);
            }

            using var stream = File.OpenRead(path);
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
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
            var accent = new AccentPolicy
            {
                AccentState = AccentEnableAcrylicBlurBehind,
                AccentFlags = 0,
                GradientColor = ToAbgr(_darkTheme
                    ? Color.FromArgb(148, 10, 10, 14)
                    : Color.FromArgb(112, 245, 245, 248))
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

        var scaleFactor = Math.Max(Width / (float)BaseClientSizePx, Height / (float)BaseClientSizePx);
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

    private void DrawIconHalo(Graphics graphics)
    {
        var horizontalHalo = Math.Max(16, (int)Math.Round(_pictureBox.Width * 0.28));
        var verticalHalo = Math.Max(12, (int)Math.Round(_pictureBox.Height * 0.18));
        var haloBounds = new RectangleF(
            _pictureBox.Left - horizontalHalo,
            _pictureBox.Top - verticalHalo,
            _pictureBox.Width + (horizontalHalo * 2),
            _pictureBox.Height + (verticalHalo * 2.6f));

        using var haloPath = new GraphicsPath();
        haloPath.AddEllipse(haloBounds);
        using var haloBrush = new PathGradientBrush(haloPath)
        {
            CenterColor = _darkTheme ? Color.FromArgb(18, 255, 255, 255) : Color.FromArgb(14, 255, 255, 255),
            SurroundColors = [Color.Transparent]
        };

        graphics.FillPath(haloBrush, haloPath);
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
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
}


