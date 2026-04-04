using Microsoft.UI.Xaml.Controls;
using FnMappingTool.Core.Models;
using FnMappingTool.Core.Services;
using FnMappingTool.Controller.Services;

namespace FnMappingTool.Controller.Views;

public sealed partial class AddKeyDialog : ContentDialog
{
    private readonly FnMappingToolController _controller;

    public AddKeyDialog(FnMappingToolController controller)
    {
        _controller = controller;
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryButtonClick;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    public InputEvent? CapturedEvent { get; private set; }

    public string KeyName => NameTextBox.Text.Trim();

    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        CaptureInfoBar.Message = "Waiting for the next OEM event...";
        CaptureInfoBar.Severity = InfoBarSeverity.Informational;
        CaptureProgressRing.IsActive = true;
        IsPrimaryButtonEnabled = false;

        var captured = await _controller.CaptureNextEventAsync();
        if (captured is null)
        {
            CaptureInfoBar.Severity = InfoBarSeverity.Warning;
            CaptureInfoBar.Message = "Capture was cancelled or timed out.";
            return;
        }

        CapturedEvent = captured;
        TriggerTextBox.Text = EventMatcherConfiguration.FromInputEvent(captured).ToDisplayText();
        CaptureInfoBar.Severity = InfoBarSeverity.Success;
        CaptureInfoBar.Message = "OEM event captured. Enter a unique key name and add it.";
        CaptureProgressRing.IsActive = false;
        Validate();
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

        if (duplicate)
        {
            CaptureInfoBar.Severity = InfoBarSeverity.Error;
            CaptureInfoBar.Message = "This key name already exists.";
        }
        else if (captured)
        {
            CaptureInfoBar.Severity = InfoBarSeverity.Success;
            CaptureInfoBar.Message = "OEM event captured. Enter a unique key name and add it.";
        }

        IsPrimaryButtonEnabled = hasName && captured && !duplicate;
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
        if (CapturedEvent is null)
        {
            _controller.CancelCapture();
        }
    }
}
