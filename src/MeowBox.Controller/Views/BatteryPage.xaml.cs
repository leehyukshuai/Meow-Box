using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MeowBox.Controller.Services;
using MeowBox.Core.Models;
using Windows.System;
using MeowBox.Core.Services;

namespace MeowBox.Controller.Views;

public sealed partial class BatteryPage : Page
{
    private const string PerformanceCatLightFill = "#4F463E";
    private const string PerformanceCatDarkFill = "#D8E0EA";
    private const double PerformanceCatLightOpacity = 0.24;
    private const double PerformanceCatDarkOpacity = 0.42;

    private bool _isLoading;
    private bool _isActive;
    private bool _isChargeLimitPointerInteraction;
    private bool _isChargeLimitKeyboardInteraction;
    private bool _isApplyingChargeLimit;
    private int? _requestedChargeLimitPercent;
    private double? _cachedChargeLimitThumbHalfWidth;
    private CancellationTokenSource? _pageLifetimeCts;

    public MeowBoxController Controller => App.Controller;

    public BatteryPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isActive = true;
        _pageLifetimeCts?.Cancel();
        _pageLifetimeCts?.Dispose();
        _pageLifetimeCts = new CancellationTokenSource();
        Controller.PropertyChanged += OnControllerPropertyChanged;
        ActualThemeChanged += OnActualThemeChanged;
        ApplyStaticLabels();
        UpdatePerformanceCatArtSources();
        SyncState();
        _ = InitializeBatteryControlsAsync(_pageLifetimeCts.Token);
        DispatcherQueue.TryEnqueue(UpdateChargeLimitTickLabelsLayout);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isActive = false;
        Controller.PropertyChanged -= OnControllerPropertyChanged;
        ActualThemeChanged -= OnActualThemeChanged;
        _pageLifetimeCts?.Cancel();
        _pageLifetimeCts?.Dispose();
        _pageLifetimeCts = null;
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MeowBoxController.BatteryControlBusy) or
            nameof(MeowBoxController.BatteryControlStatusMessage) or
            nameof(MeowBoxController.BatteryControlSupported) or
            nameof(MeowBoxController.BatteryStateKnown) or
            nameof(MeowBoxController.CurrentPerformanceModeKey) or
            nameof(MeowBoxController.CurrentPerformanceSelectionKey) or
            nameof(MeowBoxController.CurrentChargeLimitPercent) or
            nameof(MeowBoxController.SwitchToBatteryModeOnDcThresholdPercent) or
            nameof(MeowBoxController.ApplyChargeLimitOnStartup) or
            nameof(MeowBoxController.ShowEasterEggs) or
            nameof(MeowBoxController.ServiceState) or
            nameof(MeowBoxController.WorkerElevated) or
            nameof(MeowBoxController.ServiceRunning))
        {
            if (_isActive)
            {
                DispatcherQueue.TryEnqueue(SyncState);
            }
        }
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        UpdatePerformanceCatArtSources();
    }

    private void ApplyStaticLabels()
    {
        PerformanceSilentButton.Content = CreatePerformanceModeButtonContent(
            ResourceStringService.GetString("PerformanceSilentInfoBar.Title", "Silent mode"),
            "\uE708");
        PerformanceSmartButton.Content = CreatePerformanceModeButtonContent(
            ResourceStringService.GetString("PerformanceSmartInfoBar.Title", "Smart mode"),
            "\uF1BA");
        PerformanceExtremeButton.Content = CreatePerformanceModeButtonContent(
            ResourceStringService.GetString("PerformanceExtremeInfoBar.Title", "Extreme mode"),
            "\uE945");
        UpdateChargeLimitTickLabelsLayout();
    }

    private void SyncState()
    {
        _isLoading = true;

        var canShowRuntimeState = Controller.ServiceRunning && Controller.WorkerElevated && Controller.BatteryStateKnown;
        var canShowBatteryControls = canShowRuntimeState && Controller.BatteryControlSupported;
        var showNotice = !canShowBatteryControls;

        RuntimeNoticeInfoBar.Title = BuildRuntimeStateText();
        RuntimeNoticeInfoBar.Message = Controller.BatteryControlStatusMessage;
        RuntimeNoticeInfoBar.Severity = ResolveRuntimeNoticeSeverity();
        RefreshButton.IsEnabled = Controller.ServiceRunning && Controller.WorkerElevated && !Controller.BatteryControlBusy;
        RefreshButton.Visibility = Controller.ServiceRunning && Controller.WorkerElevated ? Visibility.Visible : Visibility.Collapsed;

        RuntimeNoticeInfoBar.IsOpen = showNotice;
        PerformanceCard.Visibility = canShowBatteryControls ? Visibility.Visible : Visibility.Collapsed;
        ChargeCard.Visibility = canShowBatteryControls ? Visibility.Visible : Visibility.Collapsed;

        var controlsEnabled = Controller.BatteryControlsEnabled;
        var batterySaverTriggered = string.Equals(
            BatteryControlCatalog.NormalizeSelectedPerformanceModeKey(Controller.CurrentPerformanceSelectionKey),
            BatteryControlCatalog.Battery,
            StringComparison.OrdinalIgnoreCase);
        var performanceModeButtonsEnabled = controlsEnabled && !batterySaverTriggered;
        PerformanceSilentButton.IsEnabled = performanceModeButtonsEnabled;
        PerformanceSmartButton.IsEnabled = performanceModeButtonsEnabled;
        PerformanceExtremeButton.IsEnabled = performanceModeButtonsEnabled;

        SyncPerformanceModeSelectionUi(Controller.CurrentPerformanceSelectionKey, batterySaverTriggered);
        SetSelectedChargeLimit(_requestedChargeLimitPercent ?? Controller.CurrentChargeLimitPercent);
        SetSelectedComboBoxTag(SwitchToBatteryModeOnDcComboBox, Controller.SwitchToBatteryModeOnDcThresholdPercent);
        ChargeStartupApplyToggleSwitch.IsOn = Controller.ApplyChargeLimitOnStartup;

        ChargeLimitSlider.IsEnabled = controlsEnabled;
        PerformanceCycleSettingsPanel.IsHitTestVisible = !Controller.BatteryControlBusy;
        PerformanceCycleSettingsPanel.Opacity = Controller.BatteryControlBusy ? 0.6 : 1;
        SwitchToBatteryModeOnDcComboBox.IsEnabled = !Controller.BatteryControlBusy;
        ChargeStartupApplyToggleSwitch.IsEnabled = !Controller.BatteryControlBusy;

        _isLoading = false;
    }

    private async Task InitializeBatteryControlsAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (Controller.BatteryControlBusy)
        {
            return;
        }

        if (!Controller.ServiceRunning || !Controller.WorkerElevated || (!forceRefresh && Controller.BatteryStateKnown))
        {
            return;
        }

        try
        {
            await Controller.RefreshBatteryControlStateAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!_isActive || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await ShowMessageAsync(
                ResourceStringService.GetString("Battery.Error.Failed", "Battery controls failed"),
                exception.Message);
        }
    }

    private async void OnRefreshButtonClick(object sender, RoutedEventArgs e)
    {
        if (!Controller.ServiceRunning || !Controller.WorkerElevated)
        {
            SyncState();
            return;
        }

        await InitializeBatteryControlsAsync(_pageLifetimeCts?.Token ?? CancellationToken.None, forceRefresh: true);
    }

    private async void OnPerformanceModeButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string modeKey)
        {
            return;
        }

        if (_isLoading || string.Equals(modeKey, Controller.CurrentPerformanceSelectionKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SyncPerformanceModeSelectionUi(modeKey);

        try
        {
            await Controller.SetPerformanceModeAsync(modeKey);
        }
        catch (Exception exception)
        {
            SyncState();
            await ShowMessageAsync(
                ResourceStringService.GetString("Battery.Error.CouldNotChangePerformance", "Could not change performance mode"),
                exception.Message);
        }
    }

    private void OnChargeLimitSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        var percent = NormalizeChargeLimit(e.NewValue);
        if (_isLoading || !_isActive || !Controller.BatteryStateKnown || !Controller.BatteryControlSupported)
        {
            return;
        }

        if (percent == Controller.CurrentChargeLimitPercent)
        {
            CancelPendingChargeLimitUpdate();
            return;
        }

        _requestedChargeLimitPercent = percent;
        if (!_isChargeLimitPointerInteraction && !_isChargeLimitKeyboardInteraction)
        {
            _ = ApplyPendingChargeLimitAsync();
        }
    }

    private void OnChargeLimitSliderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateChargeLimitTickLabelsLayout();
    }

    private void OnChargeLimitSliderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isChargeLimitPointerInteraction = true;
    }

    private void OnChargeLimitSliderPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isChargeLimitPointerInteraction = false;
        _ = ApplyPendingChargeLimitAsync();
    }

    private void OnChargeLimitSliderPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isChargeLimitPointerInteraction = false;
        _ = ApplyPendingChargeLimitAsync();
    }

    private void OnChargeLimitSliderLostFocus(object sender, RoutedEventArgs e)
    {
        _isChargeLimitPointerInteraction = false;
        _isChargeLimitKeyboardInteraction = false;
        _ = ApplyPendingChargeLimitAsync();
    }

    private void OnChargeLimitSliderKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsChargeLimitAdjustmentKey(e.Key))
        {
            _isChargeLimitKeyboardInteraction = true;
        }
    }

    private void OnChargeLimitSliderKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (!IsChargeLimitAdjustmentKey(e.Key))
        {
            return;
        }

        _isChargeLimitKeyboardInteraction = false;
        _ = ApplyPendingChargeLimitAsync();
    }

    private void OnChargeStartupApplyToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Controller.SetApplyChargeLimitOnStartup(ChargeStartupApplyToggleSwitch.IsOn);
    }

    private void OnSwitchToBatteryModeOnDcSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || SwitchToBatteryModeOnDcComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        Controller.SetSwitchToBatteryModeOnDcThresholdPercent(ParseComboBoxItemTag(item.Tag));
    }

    private void OnPerformanceCycleModeIncludeClick(object sender, RoutedEventArgs e)
    {
        if (_isLoading || sender is not CheckBox checkBox || checkBox.Tag is not string modeKey)
        {
            return;
        }

        Controller.SetPerformanceCycleModeIncluded(modeKey, checkBox.IsChecked == true);
    }

    private void OnPerformanceCycleMoveUpClick(object sender, RoutedEventArgs e)
    {
        if (_isLoading || sender is not FrameworkElement element || element.Tag is not string modeKey)
        {
            return;
        }

        Controller.MovePerformanceCycleModeUp(modeKey);
    }

    private void OnPerformanceCycleMoveDownClick(object sender, RoutedEventArgs e)
    {
        if (_isLoading || sender is not FrameworkElement element || element.Tag is not string modeKey)
        {
            return;
        }

        Controller.MovePerformanceCycleModeDown(modeKey);
    }

    private void SyncPerformanceModeSelectionUi(string modeKey, bool batterySaverTriggered = false)
    {
        var normalized = BatteryControlCatalog.NormalizeSelectedPerformanceModeKey(modeKey);
        if (batterySaverTriggered ||
            string.Equals(normalized, BatteryControlCatalog.Battery, StringComparison.OrdinalIgnoreCase))
        {
            ApplyPerformanceModeButtonSelectionState(PerformanceSilentButton, selected: false);
            ApplyPerformanceModeButtonSelectionState(PerformanceSmartButton, selected: false);
            ApplyPerformanceModeButtonSelectionState(PerformanceExtremeButton, selected: false);
            PerformanceModeDescriptionTextBlock.Text = ResourceStringService.GetString("PerformanceModeDisabledByBatterySaver.Message", string.Empty);
            SetPerformanceCatArt(null);
            return;
        }

        var selectedButton = normalized switch
        {
            var key when string.Equals(key, BatteryControlCatalog.Silent, StringComparison.OrdinalIgnoreCase) => PerformanceSilentButton,
            var key when string.Equals(key, BatteryControlCatalog.Extreme, StringComparison.OrdinalIgnoreCase) => PerformanceExtremeButton,
            _ => PerformanceSmartButton
        };

        ApplyPerformanceModeButtonSelectionState(PerformanceSilentButton, ReferenceEquals(selectedButton, PerformanceSilentButton));
        ApplyPerformanceModeButtonSelectionState(PerformanceSmartButton, ReferenceEquals(selectedButton, PerformanceSmartButton));
        ApplyPerformanceModeButtonSelectionState(PerformanceExtremeButton, ReferenceEquals(selectedButton, PerformanceExtremeButton));

        var messageKey = normalized switch
        {
            var key when string.Equals(key, BatteryControlCatalog.Silent, StringComparison.OrdinalIgnoreCase) => "PerformanceSilentInfoBar.Message",
            var key when string.Equals(key, BatteryControlCatalog.Extreme, StringComparison.OrdinalIgnoreCase) => "PerformanceExtremeInfoBar.Message",
            _ => "PerformanceSmartInfoBar.Message"
        };

        PerformanceModeDescriptionTextBlock.Text = ResourceStringService.GetString(messageKey, string.Empty);
        SetPerformanceCatArt(normalized);
    }

    private void SetPerformanceCatArt(string? normalizedModeKey)
    {
        PerformanceSilentCatCanvas.Visibility = Visibility.Collapsed;
        PerformanceSmartCatCanvas.Visibility = Visibility.Collapsed;
        PerformanceExtremeCatCanvas.Visibility = Visibility.Collapsed;

        if (!Controller.ShowEasterEggs)
        {
            return;
        }

        if (string.Equals(normalizedModeKey, BatteryControlCatalog.Silent, StringComparison.OrdinalIgnoreCase))
        {
            PerformanceSilentCatCanvas.Visibility = Visibility.Visible;
            return;
        }

        if (string.Equals(normalizedModeKey, BatteryControlCatalog.Extreme, StringComparison.OrdinalIgnoreCase))
        {
            PerformanceExtremeCatCanvas.Visibility = Visibility.Visible;
            return;
        }

        if (!string.IsNullOrWhiteSpace(normalizedModeKey))
        {
            PerformanceSmartCatCanvas.Visibility = Visibility.Visible;
        }
    }

    private async void UpdatePerformanceCatArtSources()
    {
        var theme = ActualTheme;
        var fillColor = theme == ElementTheme.Light
            ? PerformanceCatLightFill
            : PerformanceCatDarkFill;
        var opacity = theme == ElementTheme.Light
            ? PerformanceCatLightOpacity
            : PerformanceCatDarkOpacity;

        PerformanceSilentCatCanvas.Opacity = opacity;
        PerformanceSmartCatCanvas.Opacity = opacity;
        PerformanceExtremeCatCanvas.Opacity = opacity;

        var silentSource = await SvgAssetTintService.CreateTintedImageSourceAsync("cat-stand.svg", fillColor);
        var smartSource = await SvgAssetTintService.CreateTintedImageSourceAsync("cat-walk.svg", fillColor);
        var extremeSource = await SvgAssetTintService.CreateTintedImageSourceAsync("cat-run.svg", fillColor);

        if (!_isActive || ActualTheme != theme)
        {
            return;
        }

        PerformanceSilentCatImage.Source = silentSource;
        PerformanceSmartCatImage.Source = smartSource;
        PerformanceExtremeCatImage.Source = extremeSource;
    }

    private void SetSelectedChargeLimit(int percent)
    {
        var normalized = Math.Max(60, BatteryControlCatalog.NormalizeChargeLimitPercent(percent));
        if (Math.Abs(ChargeLimitSlider.Value - normalized) < 0.1)
        {
            return;
        }

        ChargeLimitSlider.Value = normalized;
    }

    private int NormalizeChargeLimit(double value)
    {
        var rounded = (int)Math.Round(value / 10d, MidpointRounding.AwayFromZero) * 10;
        return Math.Max(60, BatteryControlCatalog.NormalizeChargeLimitPercent(rounded));
    }

    private void CancelPendingChargeLimitUpdate()
    {
        _requestedChargeLimitPercent = null;
    }

    private static bool IsChargeLimitAdjustmentKey(VirtualKey key)
    {
        return key is VirtualKey.Left or
            VirtualKey.Right or
            VirtualKey.Up or
            VirtualKey.Down or
            VirtualKey.Home or
            VirtualKey.End or
            VirtualKey.PageUp or
            VirtualKey.PageDown;
    }

    private static int ParseComboBoxItemTag(object? tag)
    {
        return tag is string value && int.TryParse(value, out var parsed)
            ? parsed
            : BatteryControlCatalog.AutoSwitchNeverThreshold;
    }

    private static void SetSelectedComboBoxTag(ComboBox comboBox, int selectedTag)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (ParseComboBoxItemTag(item.Tag) != selectedTag)
            {
                continue;
            }

            comboBox.SelectedItem = item;
            return;
        }

        comboBox.SelectedIndex = 0;
    }

    private async Task ApplyPendingChargeLimitAsync()
    {
        if (_isApplyingChargeLimit || _requestedChargeLimitPercent is not int percent)
        {
            return;
        }

        if (percent == Controller.CurrentChargeLimitPercent)
        {
            _requestedChargeLimitPercent = null;
            return;
        }

        _isApplyingChargeLimit = true;
        try
        {
            var applied = await Controller.SetChargeLimitPercentAsync(percent);
            if (_requestedChargeLimitPercent == percent || !applied)
            {
                _requestedChargeLimitPercent = null;
            }
        }
        finally
        {
            _isApplyingChargeLimit = false;
            if (!_isChargeLimitPointerInteraction &&
                !_isChargeLimitKeyboardInteraction &&
                _requestedChargeLimitPercent is int pendingPercent &&
                pendingPercent != Controller.CurrentChargeLimitPercent)
            {
                _ = ApplyPendingChargeLimitAsync();
            }
        }
    }

    private static object CreatePerformanceModeButtonContent(string label, string glyph)
    {
        var content = new Grid
        {
            ColumnSpacing = 0
        };
        content.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(3)
        });
        content.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        var indicator = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            CornerRadius = new CornerRadius(8, 0, 0, 8),
            Margin = new Thickness(0)
        };
        content.Children.Add(indicator);

        var body = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(6, 8, 10, 8)
        };
        Grid.SetColumn(body, 1);
        body.Children.Add(new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Glyph = glyph,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        });
        body.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(body);

        return content;
    }

    private static void ApplyPerformanceModeButtonSelectionState(Button button, bool selected)
    {
        button.Style = (Style)Application.Current.Resources["SubtleButtonStyle"];
        button.BorderThickness = new Thickness(1);
        var enabled = button.IsEnabled;
        var accentColor = GetPerformanceModeAccentColor(button.Tag as string);
        button.BorderBrush = selected && enabled
            ? new SolidColorBrush(ColorHelper.FromArgb(160, accentColor.R, accentColor.G, accentColor.B))
            : (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
        button.Background = selected && enabled
            ? new SolidColorBrush(ColorHelper.FromArgb(22, accentColor.R, accentColor.G, accentColor.B))
            : (Brush)Application.Current.Resources["LayerFillColorAltBrush"];
        button.Opacity = enabled ? 1 : 0.9;

        if (button.Content is not Grid content || content.Children.Count < 2)
        {
            return;
        }

        if (content.Children[0] is Border indicator)
        {
            indicator.Background = selected && enabled
                ? new SolidColorBrush(accentColor)
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        if (content.Children[1] is StackPanel body)
        {
            var normalBrush = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            var disabledBrush = (Brush)Application.Current.Resources["TextFillColorDisabledBrush"];
            foreach (var child in body.Children)
            {
                switch (child)
                {
                    case TextBlock textBlock:
                        textBlock.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                        textBlock.Foreground = enabled ? normalBrush : disabledBrush;
                        break;
                    case FontIcon icon:
                        icon.Foreground = !enabled
                            ? disabledBrush
                            : selected
                            ? new SolidColorBrush(accentColor)
                            : normalBrush;
                        break;
                }
            }
        }
    }

    private static Windows.UI.Color GetPerformanceModeAccentColor(string? modeKey)
    {
        var normalized = BatteryControlCatalog.NormalizeSelectedPerformanceModeKey(modeKey);
        return normalized switch
        {
            var key when string.Equals(key, BatteryControlCatalog.Silent, StringComparison.OrdinalIgnoreCase) => ColorHelper.FromArgb(255, 92, 146, 220),
            var key when string.Equals(key, BatteryControlCatalog.Extreme, StringComparison.OrdinalIgnoreCase) => ColorHelper.FromArgb(255, 217, 146, 78),
            _ => ColorHelper.FromArgb(255, 88, 166, 103)
        };
    }

    private void UpdateChargeLimitTickLabelsLayout()
    {
        if (!ChargeLimitSlider.IsLoaded || !ChargeLimitTickLabelsCanvas.IsLoaded)
        {
            return;
        }

        var labels = GetChargeLimitTickLabels().ToArray();
        if (labels.Length == 0)
        {
            return;
        }

        var sliderWidth = ChargeLimitSlider.ActualWidth;
        if (sliderWidth <= 0)
        {
            return;
        }

        var thumbHalfWidth = GetChargeLimitThumbHalfWidth();
        var usableWidth = Math.Max(0, sliderWidth - (thumbHalfWidth * 2));
        var stepCount = labels.Length - 1;
        for (var index = 0; index < labels.Length; index++)
        {
            var label = labels[index];
            label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var labelWidth = label.DesiredSize.Width;
            var center = thumbHalfWidth + (usableWidth * index / stepCount);
            Canvas.SetLeft(label, center - (labelWidth / 2));
        }
    }

    private double GetChargeLimitThumbHalfWidth()
    {
        if (_cachedChargeLimitThumbHalfWidth.HasValue)
        {
            return _cachedChargeLimitThumbHalfWidth.Value;
        }

        var thumb = FindDescendant<Thumb>(ChargeLimitSlider);
        _cachedChargeLimitThumbHalfWidth = thumb is not null && thumb.ActualWidth > 0
            ? thumb.ActualWidth / 2
            : 10;
        return _cachedChargeLimitThumbHalfWidth.Value;
    }

    private IEnumerable<TextBlock> GetChargeLimitTickLabels()
    {
        yield return Charge60Label;
        yield return Charge70Label;
        yield return Charge80Label;
        yield return Charge90Label;
        yield return Charge100Label;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(root); childIndex++)
        {
            var child = VisualTreeHelper.GetChild(root, childIndex);
            if (child is T target)
            {
                return target;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private string BuildRuntimeStateText()
    {
        if (Controller.BatteryControlBusy)
        {
            return ResourceStringService.GetString("Battery.Runtime.Preparing", "Preparing admin runtime...");
        }

        if (!Controller.ServiceRunning)
        {
            return ResourceStringService.GetString("Battery.Runtime.NotRunning", "Background service is not running.");
        }

        if (Controller.ServiceState == WorkerServiceState.Starting)
        {
            return ResourceStringService.GetString("Battery.Runtime.WorkerStarting", "Worker is starting...");
        }

        if (Controller.ServiceState == WorkerServiceState.Stopping)
        {
            return ResourceStringService.GetString("Battery.Runtime.WorkerStopping", "Worker is stopping...");
        }

        if (!Controller.WorkerElevated)
        {
            return ResourceStringService.GetString("Battery.Runtime.NeedsAdmin", "Background service needs admin rights.");
        }

        if (!Controller.BatteryStateKnown)
        {
            return ResourceStringService.GetString("Battery.Runtime.WaitingStatus", "Waiting for current power status...");
        }

        if (Controller.WorkerElevated && Controller.ServiceRunning)
        {
            return ResourceStringService.GetString("Battery.Runtime.AdminActive", "Admin runtime is active.");
        }

        return ResourceStringService.GetString("Battery.Runtime.AdminInactive", "Admin runtime is inactive.");
    }

    private InfoBarSeverity ResolveRuntimeNoticeSeverity()
    {
        return InfoBarSeverity.Informational;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        if (!_isActive)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            RequestedTheme = App.ThemeService.GetResolvedTheme(),
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = ResourceStringService.GetString("Dialog.Close", "Close")
        };

        await dialog.ShowAsync();
    }

    private Task InvokeOnUiThreadAsync(Func<Task> action)
    {
        var completionSource = new TaskCompletionSource<object?>();
        if (!DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    completionSource.TrySetResult(null);
                }
                catch (Exception exception)
                {
                    completionSource.TrySetException(exception);
                }
            }))
        {
            completionSource.TrySetException(new InvalidOperationException(ResourceStringService.GetString("Error.UIThread", "Could not switch to the UI thread.")));
        }

        return completionSource.Task;
    }
}
