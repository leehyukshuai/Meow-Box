using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MeowBox.Controller.Services;
using MeowBox.Core.Models;
using Windows.System;

namespace MeowBox.Controller.Views;

public sealed partial class BatteryPage : Page
{
    private bool _isLoading;
    private bool _isActive;
    private bool _isChargeLimitPointerInteraction;
    private bool _isChargeLimitKeyboardInteraction;
    private bool _isApplyingChargeLimit;
    private int? _requestedChargeLimitPercent;
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
        ApplyStaticLabels();
        SyncState();
        _ = InitializeBatteryControlsAsync(_pageLifetimeCts.Token);
        DispatcherQueue.TryEnqueue(() =>
        {
            XamlStringLocalizer.Apply(this);
            UpdateChargeLimitTickLabelsLayout();
        });
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isActive = false;
        Controller.PropertyChanged -= OnControllerPropertyChanged;
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
            nameof(MeowBoxController.CurrentChargeLimitPercent) or
            nameof(MeowBoxController.ResetPerformanceModeToSmartOnStartup) or
            nameof(MeowBoxController.ResetChargeLimitToFullOnStartup) or
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

    private void ApplyStaticLabels()
    {
        PerformanceSilentRadioButton.Content = BatteryControlCatalog.GetPerformanceModeLabel(BatteryControlCatalog.Silent);
        PerformanceSmartRadioButton.Content = BatteryControlCatalog.GetPerformanceModeLabel(BatteryControlCatalog.Smart);
        PerformanceBeastRadioButton.Content = BatteryControlCatalog.GetPerformanceModeLabel(BatteryControlCatalog.Beast);
        PerformanceStartupResetLabelTextBlock.Text = LocalizedText.Pick("Switch to Smart mode on startup", "开机时自动切换至智能模式");
        ChargeStartupResetLabelTextBlock.Text = LocalizedText.Pick("Disable charge limit protection on startup", "开机时关闭充电上限保护");
        UpdateChargeLimitTickLabelsLayout();
    }

    private void SyncState()
    {
        _isLoading = true;

        var canShowRuntimeState = Controller.ServiceRunning && Controller.WorkerElevated && Controller.BatteryStateKnown;
        var canShowBatteryControls = canShowRuntimeState && Controller.BatteryControlSupported;
        var showNotice = !canShowBatteryControls;

        BatteryStatusTextBlock.Text = Controller.BatteryControlStatusMessage;
        RuntimeStateTextBlock.Text = BuildRuntimeStateText();
        StatusProgressRing.IsActive = Controller.BatteryControlBusy;
        RefreshButton.IsEnabled = Controller.ServiceRunning && Controller.WorkerElevated && !Controller.BatteryControlBusy;
        RefreshButton.Visibility = Controller.ServiceRunning && Controller.WorkerElevated ? Visibility.Visible : Visibility.Collapsed;

        RuntimeNoticeCard.Visibility = showNotice ? Visibility.Visible : Visibility.Collapsed;
        PerformanceCard.Visibility = canShowBatteryControls ? Visibility.Visible : Visibility.Collapsed;
        ChargeCard.Visibility = canShowBatteryControls ? Visibility.Visible : Visibility.Collapsed;

        SetSelectedPerformanceMode(Controller.CurrentPerformanceModeKey);
        SetSelectedChargeLimit(_requestedChargeLimitPercent ?? Controller.CurrentChargeLimitPercent);
        PerformanceStartupResetToggleSwitch.IsOn = Controller.ResetPerformanceModeToSmartOnStartup;
        ChargeStartupResetToggleSwitch.IsOn = Controller.ResetChargeLimitToFullOnStartup;

        var controlsEnabled = Controller.BatteryControlsEnabled;
        foreach (var radioButton in GetPerformanceModeButtons())
        {
            radioButton.IsEnabled = controlsEnabled;
        }

        ChargeLimitSlider.IsEnabled = controlsEnabled;
        PerformanceStartupResetToggleSwitch.IsEnabled = !Controller.BatteryControlBusy;
        ChargeStartupResetToggleSwitch.IsEnabled = !Controller.BatteryControlBusy;

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
                LocalizedText.Pick("Battery controls failed", "电池控制失败"),
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

    private async void OnPerformanceModeChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radioButton || radioButton.Tag is not string modeKey)
        {
            return;
        }

        if (_isLoading || string.Equals(modeKey, Controller.CurrentPerformanceModeKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await Controller.SetPerformanceModeAsync(modeKey);
        }
        catch (Exception exception)
        {
            SyncState();
            await ShowMessageAsync(
                LocalizedText.Pick("Could not change performance mode", "无法切换性能模式"),
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

    private void OnPerformanceStartupResetToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Controller.SetResetPerformanceModeToSmartOnStartup(PerformanceStartupResetToggleSwitch.IsOn);
    }

    private void OnChargeStartupResetToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Controller.SetResetChargeLimitToFullOnStartup(ChargeStartupResetToggleSwitch.IsOn);
    }

    private void SetSelectedPerformanceMode(string modeKey)
    {
        var normalized = BatteryControlCatalog.NormalizePerformanceModeKey(modeKey);
        foreach (var radioButton in GetPerformanceModeButtons())
        {
            radioButton.IsChecked = string.Equals(radioButton.Tag as string, normalized, StringComparison.OrdinalIgnoreCase);
        }
    }

    private IEnumerable<RadioButton> GetPerformanceModeButtons()
    {
        yield return PerformanceSilentRadioButton;
        yield return PerformanceSmartRadioButton;
        yield return PerformanceBeastRadioButton;
    }

    private void SetSelectedChargeLimit(int percent)
    {
        var normalized = BatteryControlCatalog.NormalizeChargeLimitPercent(percent);
        ChargeLimitSlider.Value = normalized;
    }

    private int NormalizeChargeLimit(double value)
    {
        var rounded = (int)Math.Round(value / 10d, MidpointRounding.AwayFromZero) * 10;
        return BatteryControlCatalog.NormalizeChargeLimitPercent(rounded);
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
            await Controller.SetChargeLimitPercentAsync(percent);
            if (_requestedChargeLimitPercent == percent)
            {
                _requestedChargeLimitPercent = null;
            }
        }
        catch (Exception exception)
        {
            _requestedChargeLimitPercent = null;
            SyncState();
            await ShowMessageAsync(
                LocalizedText.Pick("Could not change charge limit", "无法切换充电限制"),
                exception.Message);
        }
        finally
        {
            _isApplyingChargeLimit = false;
        }
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
        var thumb = FindDescendant<Thumb>(ChargeLimitSlider);
        return thumb is not null && thumb.ActualWidth > 0
            ? thumb.ActualWidth / 2
            : 10;
    }

    private IEnumerable<TextBlock> GetChargeLimitTickLabels()
    {
        yield return Charge40Label;
        yield return Charge50Label;
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
            return LocalizedText.Pick("Preparing admin runtime...", "正在准备管理员后台……");
        }

        if (!Controller.ServiceRunning)
        {
            return LocalizedText.Pick("Background service is not running.", "后台服务未运行。");
        }

        if (Controller.ServiceState == WorkerServiceState.Starting)
        {
            return LocalizedText.Pick("Worker is starting...", "后台服务启动中。");
        }

        if (Controller.ServiceState == WorkerServiceState.Stopping)
        {
            return LocalizedText.Pick("Worker is stopping...", "后台服务停止中。");
        }

        if (!Controller.WorkerElevated)
        {
            return LocalizedText.Pick("Background service needs admin rights.", "后台服务需要管理员权限。");
        }

        if (!Controller.BatteryStateKnown)
        {
            return LocalizedText.Pick("Waiting for current power status...", "正在等待当前电源状态……");
        }

        if (Controller.WorkerElevated && Controller.ServiceRunning)
        {
            return LocalizedText.Pick("Admin runtime is active.", "管理员后台已启用。");
        }

        return LocalizedText.Pick("Admin runtime is inactive.", "管理员后台尚未启用。");
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        if (!_isActive)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = Localizer.GetString("Dialog.Close")
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
            completionSource.TrySetException(new InvalidOperationException(LocalizedText.Pick("Could not switch to the UI thread.", "无法切换到 UI 线程。")));
        }

        return completionSource.Task;
    }
}
