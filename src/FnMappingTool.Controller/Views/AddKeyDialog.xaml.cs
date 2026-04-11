using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using FnMappingTool.Core.Services;
using FnMappingTool.Controller.Services;

namespace FnMappingTool.Controller.Views;

public sealed partial class AddKeyDialog : ContentDialog
{
    private readonly FnMappingToolController _controller;
    private bool _isCapturing;
    private bool _isClosed;
    private int _captureVersion;

    public AddKeyDialog(FnMappingToolController controller)
    {
        _controller = controller;
        InitializeComponent();
        XamlStringLocalizer.Apply(this);
        PrimaryButtonClick += OnPrimaryButtonClick;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    public InputEvent? CapturedEvent { get; private set; }

    public string KeyName => NameTextBox.Text.Trim();

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        _isClosed = false;
        ApplyDialogButtonStyles();
        ResetToIdleState(clearName: false);
        StartCaptureButton.Focus(FocusState.Programmatic);
    }

    private async void OnStartCaptureClick(object sender, RoutedEventArgs e)
    {
        if (_isCapturing)
        {
            return;
        }

        var captureVersion = ++_captureVersion;
        CapturedEvent = null;
        _isCapturing = true;

        StartCaptureButton.IsEnabled = false;
        KeyNameCard.Visibility = Visibility.Collapsed;
        SetStatusIcon(StatusVisual.Listening, Localizer.GetString("CaptureWaitingInlineText"));
        Validate();

        var captured = await _controller.CaptureNextEventAsync();
        if (_isClosed || captureVersion != _captureVersion)
        {
            return;
        }

        _isCapturing = false;
        StartCaptureButton.IsEnabled = true;

        if (captured is null)
        {
            SetStatusIcon(StatusVisual.Warning, Localizer.GetString("AddKey.CaptureCancelled"));
            Validate();
            return;
        }

        CapturedEvent = captured;
        KeyNameCard.Visibility = Visibility.Visible;
        SetStatusIcon(StatusVisual.Success, Localizer.GetString("AddKey.CaptureSuccess"));
        Validate();
        NameTextBox.Focus(FocusState.Programmatic);
    }

    private void OnNameTextChanged(object sender, TextChangedEventArgs e)
    {
        Validate();
    }

    private void Validate()
    {
        var hasName = !string.IsNullOrWhiteSpace(KeyName);
        var duplicate = hasName && _controller.IsDuplicateKeyName(KeyName);
        var captured = CapturedEvent is not null;

        NameValidationTextBlock.Text = duplicate ? Localizer.GetString("AddKey.NameExists") : string.Empty;
        NameValidationTextBlock.Visibility = duplicate ? Visibility.Visible : Visibility.Collapsed;

        if (_isCapturing)
        {
            IsPrimaryButtonEnabled = false;
            return;
        }

        IsPrimaryButtonEnabled = hasName && captured && !duplicate;
    }

    private void ResetToIdleState(bool clearName)
    {
        ++_captureVersion;
        _isCapturing = false;
        CapturedEvent = null;
        if (clearName)
        {
            NameTextBox.Text = string.Empty;
        }

        KeyNameCard.Visibility = Visibility.Collapsed;
        NameValidationTextBlock.Text = string.Empty;
        NameValidationTextBlock.Visibility = Visibility.Collapsed;
        StartCaptureButton.IsEnabled = true;
        SetStatusIcon(StatusVisual.Idle, Localizer.GetString("AddKey.CaptureReady"));
        IsPrimaryButtonEnabled = false;
    }

    private void SetStatusIcon(StatusVisual visual, string toolTipText)
    {
        CaptureProgressRing.IsActive = visual == StatusVisual.Listening;
        CaptureProgressRing.Visibility = visual == StatusVisual.Listening ? Visibility.Visible : Visibility.Collapsed;
        CaptureStatusIcon.Visibility = visual == StatusVisual.Listening ? Visibility.Collapsed : Visibility.Visible;

        string glyph;
        string foregroundKey;
        switch (visual)
        {
            case StatusVisual.Success:
                glyph = "";
                foregroundKey = "SystemFillColorSuccessBrush";
                break;
            case StatusVisual.Warning:
                glyph = "";
                foregroundKey = "SystemFillColorCautionBrush";
                break;
            default:
                glyph = "";
                foregroundKey = "TextFillColorSecondaryBrush";
                break;
        }

        CaptureStatusIcon.Glyph = glyph;
        var foreground = Application.Current.Resources[foregroundKey] as Brush
            ?? Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush;
        CaptureStatusIcon.Foreground = foreground;
        ToolTipService.SetToolTip(CaptureStatusBadge, toolTipText);
    }

    private void ApplyDialogButtonStyles()
    {
        if (Application.Current.Resources["AccentButtonStyle"] is Style accentStyle && GetTemplateChild("PrimaryButton") is Button primaryButton)
        {
            primaryButton.Style = accentStyle;
        }

        if (Application.Current.Resources["SubtleButtonStyle"] is Style subtleStyle && GetTemplateChild("CloseButton") is Button closeButton)
        {
            closeButton.Style = subtleStyle;
        }
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (CapturedEvent is null || string.IsNullOrWhiteSpace(KeyName) || _controller.IsDuplicateKeyName(KeyName))
        {
            args.Cancel = true;
        }
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        _isClosed = true;
        ++_captureVersion;
        if (_isCapturing)
        {
            _controller.CancelCapture();
        }
    }

    private enum StatusVisual
    {
        Idle,
        Listening,
        Success,
        Warning
    }
}


