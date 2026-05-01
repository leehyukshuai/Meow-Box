using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using MeowBox.Controller.Services;
using MeowBox.Controller.ViewModels;
using MeowBox.Core.Models;
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;
using MeowBox.Core.Services;

namespace MeowBox.Controller.Views;

public sealed partial class TouchpadPage : Page
{
    private const double PressureScaleMax = 2500d;
    private const double DeepPressThreshold = RuntimeDefaults.DefaultTouchpadDeepPressThreshold;
    private const double RedPressureThreshold = 800d;
    private const double PreviewStrokeThickness = 1.15d;
    private const string TouchpadPawLightFill = "#C8B8A4";
    private const string TouchpadPawDarkFill = "#3A4250";
    private double _displayPressure;
    private DateTimeOffset _lastDisplayPressureAt;
    private double _pressureDescriptionValue;
    private DateTimeOffset _lastPressureDescriptionAt;
    private bool _renderPending;
    private bool _touchpadStateRefreshPending;
    private bool _touchpadPreferencesLoading;
    private CancellationTokenSource? _pressSensitivityApplyCts;
    private CancellationTokenSource? _feedbackApplyCts;
    private readonly SemaphoreSlim _touchpadHardwareGate = new(1, 1);
    private readonly Dictionary<int, SmoothedContactState> _smoothedContacts = [];

    public MeowBoxController Controller => App.Controller;

    public ObservableCollection<TouchpadLiveContactViewModel> VisibleContacts { get; } = [];

    public TouchpadPage()
    {
        InitializeComponent();
        DataContext = Controller;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Controller.PropertyChanged += OnControllerPropertyChanged;
        App.ThemeService.ResolvedThemeChanged += OnResolvedThemeChanged;
        SubscribeTouchpadState();
        SubscribeTouchpadActionEditors();
        SyncTouchpadPreferenceControls();
        UpdateTouchpadPawSource();
        ScheduleTouchpadStateRefresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Controller.PropertyChanged -= OnControllerPropertyChanged;
        App.ThemeService.ResolvedThemeChanged -= OnResolvedThemeChanged;
        UnsubscribeTouchpadState();
        UnsubscribeTouchpadActionEditors();
        _pressSensitivityApplyCts?.Cancel();
        _feedbackApplyCts?.Cancel();
    }

    private void OnResolvedThemeChanged(object? sender, ElementTheme e)
    {
        UpdateTouchpadPawSource();
        RequestRender();
    }

    private void SubscribeTouchpadState()
    {
        Controller.TouchpadLive.PropertyChanged += OnTouchpadLivePropertyChanged;
        Controller.TouchpadLive.Contacts.CollectionChanged += OnTouchpadContactsChanged;
    }

    private void UnsubscribeTouchpadState()
    {
        Controller.TouchpadLive.PropertyChanged -= OnTouchpadLivePropertyChanged;
        Controller.TouchpadLive.Contacts.CollectionChanged -= OnTouchpadContactsChanged;
    }

    private void SubscribeTouchpadActionEditors()
    {
        foreach (var editor in Controller.Touchpad.AllActionEditors)
        {
            editor.Action.PropertyChanged += OnTouchpadActionPropertyChanged;
        }
    }

    private void UnsubscribeTouchpadActionEditors()
    {
        foreach (var editor in Controller.Touchpad.AllActionEditors)
        {
            editor.Action.PropertyChanged -= OnTouchpadActionPropertyChanged;
        }
    }

    private void OnTouchpadActionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Controller.IsReloadingConfiguration)
        {
            return;
        }

        if (e.PropertyName is nameof(ActionDefinitionViewModel.Type) or
            nameof(ActionDefinitionViewModel.PrimaryKey) or
            nameof(ActionDefinitionViewModel.PrimaryKeyGroup) or
            nameof(ActionDefinitionViewModel.ModifierSelectionSignature) or
            nameof(ActionDefinitionViewModel.Target) or
            nameof(ActionDefinitionViewModel.Arguments))
        {
            TrySaveTouchpadConfigurationAsync();
        }
    }

    private async void OnChooseTouchpadActionClick(object sender, RoutedEventArgs e)
    {
        var editor = GetActionEditor(sender);
        if (editor is null)
        {
            return;
        }

        var actionType = await ActionEditorDialogService.PickActionTypeAsync(Content.XamlRoot, editor.Action.Type);
        if (string.IsNullOrWhiteSpace(actionType))
        {
            return;
        }

        editor.Action.Type = actionType;
        TrySaveTouchpadConfigurationAsync();
    }

    private void OnClearTouchpadActionClick(object sender, RoutedEventArgs e)
    {
        var editor = GetActionEditor(sender);
        if (editor is null)
        {
            return;
        }

        editor.Action.ClearAssignment();
        TrySaveTouchpadConfigurationAsync();
    }

    private void OnTouchpadEditorLostFocus(object sender, RoutedEventArgs e)
    {
        if (!Controller.IsReloadingConfiguration)
        {
            TrySaveTouchpadConfigurationAsync();
        }
    }

    private async void OnPickInstalledAppClick(object sender, RoutedEventArgs e)
    {
        var editor = GetActionEditor(sender);
        if (editor is null)
        {
            return;
        }

        var apps = await Controller.GetInstalledAppsAsync();
        var target = await ActionEditorDialogService.PickInstalledAppTargetAsync(Content.XamlRoot, apps);
        if (!string.IsNullOrWhiteSpace(target))
        {
            editor.Action.Target = target;
            TrySaveTouchpadConfigurationAsync();
        }
    }

    private async void OnBrowseExecutableClick(object sender, RoutedEventArgs e)
    {
        var editor = GetActionEditor(sender);
        if (editor is null)
        {
            return;
        }

        var path = await ActionEditorDialogService.PickLaunchPathAsync([".exe", ".lnk", ".bat", "*"]);
        if (!string.IsNullOrWhiteSpace(path))
        {
            editor.Action.Target = path;
            TrySaveTouchpadConfigurationAsync();
        }
    }

    private void OnTouchpadCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RequestRender();
    }

    private void OnTouchpadLongPressSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_touchpadPreferencesLoading || Controller.IsReloadingConfiguration)
        {
            return;
        }

        Controller.ApplyTouchpadPreferences(
            Controller.TouchpadLightPressThreshold,
            GetSelectedIntValue(TouchpadLongPressComboBox, 700));
    }

    private void OnTouchpadPressSensitivitySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_touchpadPreferencesLoading || Controller.IsReloadingConfiguration)
        {
            return;
        }

        var level = GetSelectedHardwareLevel(TouchpadPressSensitivityComboBox);
        if (level == Controller.TouchpadPressSensitivityLevel)
        {
            return;
        }

        DebounceTouchpadHardwareAction(ref _pressSensitivityApplyCts, async () =>
        {
            await RunTouchpadHardwareActionAsync(() => Controller.SetTouchpadHardwarePressAsync(level));
        });
    }

    private void OnTouchpadFeedbackSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_touchpadPreferencesLoading || Controller.IsReloadingConfiguration)
        {
            return;
        }

        var level = GetSelectedHardwareLevel(TouchpadFeedbackComboBox);
        if (level == Controller.TouchpadFeedbackLevel)
        {
            return;
        }

        DebounceTouchpadHardwareAction(ref _feedbackApplyCts, async () =>
        {
            await RunTouchpadHardwareActionAsync(() => Controller.SetTouchpadHardwareVibrationAsync(level));
        });
    }

    private async void OnTouchpadDeepPressFeedbackSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_touchpadPreferencesLoading || Controller.IsReloadingConfiguration)
        {
            return;
        }

        var enabled = GetSelectedBooleanValue(TouchpadDeepPressFeedbackComboBox, Controller.TouchpadDeepPressHapticsEnabled);
        if (enabled == Controller.TouchpadDeepPressHapticsEnabled)
        {
            return;
        }

        await RunTouchpadHardwareActionAsync(() => Controller.SetTouchpadHardwareHapticAsync(enabled));
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MeowBoxController.Touchpad) or
                                   nameof(MeowBoxController.TouchpadLightPressThreshold) or
                                   nameof(MeowBoxController.TouchpadLongPressDurationMs) or
                                   nameof(MeowBoxController.TouchpadPressSensitivityLevel) or
                                   nameof(MeowBoxController.TouchpadFeedbackLevel) or
                                   nameof(MeowBoxController.TouchpadDeepPressHapticsEnabled) or
                                   nameof(MeowBoxController.ShowEasterEggs)))
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (e.PropertyName == nameof(MeowBoxController.Touchpad))
            {
                UnsubscribeTouchpadActionEditors();
                SubscribeTouchpadActionEditors();
            }

            if (e.PropertyName != nameof(MeowBoxController.ShowEasterEggs))
            {
                SyncTouchpadPreferenceControls();
                ScheduleTouchpadStateRefresh();
            }

            RequestRender();
        });
    }

    private void SyncTouchpadPreferenceControls()
    {
        _touchpadPreferencesLoading = true;
        SetSelectedHardwareLevel(TouchpadPressSensitivityComboBox, Controller.TouchpadPressSensitivityLevel);
        SetSelectedHardwareLevel(TouchpadFeedbackComboBox, Controller.TouchpadFeedbackLevel);
        SetSelectedIntValue(TouchpadLongPressComboBox, Controller.TouchpadLongPressDurationMs);
        SetSelectedBooleanValue(TouchpadDeepPressFeedbackComboBox, Controller.TouchpadDeepPressHapticsEnabled);
        _touchpadPreferencesLoading = false;
    }

    private static int GetSelectedHardwareLevel(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out var level))
        {
            return TouchpadHardwareSettings.NormalizeLevel(level);
        }

        return 1;
    }

    private static void SetSelectedHardwareLevel(ComboBox comboBox, int level)
    {
        level = TouchpadHardwareSettings.NormalizeLevel(level);
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), level.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = Math.Clamp(level - 1, 0, Math.Max(0, comboBox.Items.Count - 1));
    }

    private static int GetSelectedIntValue(ComboBox comboBox, int fallback)
    {
        if (comboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out var value))
        {
            return value;
        }

        return fallback;
    }

    private static bool GetSelectedBooleanValue(ComboBox comboBox, bool fallback)
    {
        if (comboBox.SelectedItem is ComboBoxItem item &&
            bool.TryParse(item.Tag?.ToString(), out var value))
        {
            return value;
        }

        return fallback;
    }

    private static void SetSelectedIntValue(ComboBox comboBox, int value)
    {
        ComboBoxItem? closestItem = null;
        var closestDistance = int.MaxValue;

        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (!int.TryParse(item.Tag?.ToString(), out var itemValue))
            {
                continue;
            }

            var distance = Math.Abs(itemValue - value);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestItem = item;
            }
        }

        if (closestItem is not null)
        {
            comboBox.SelectedItem = closestItem;
        }
    }

    private static void SetSelectedBooleanValue(ComboBox comboBox, bool value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (bool.TryParse(item.Tag?.ToString(), out var itemValue) && itemValue == value)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = value ? 1 : 0;
    }

    private void DebounceTouchpadHardwareAction(ref CancellationTokenSource? cts, Func<Task> action)
    {
        cts?.Cancel();
        cts = new CancellationTokenSource();
        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(180, token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                await InvokeOnUiThreadAsync(action);
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private async Task RunTouchpadHardwareActionAsync(Func<Task> action)
    {
        await _touchpadHardwareGate.WaitAsync();
        try
        {
            TouchpadHardwareControlPanel.IsHitTestVisible = false;
            await action();
        }
        catch (Exception exception)
        {
            SyncTouchpadPreferenceControls();
            await ShowTouchpadErrorAsync(exception.Message);
        }
        finally
        {
            TouchpadHardwareControlPanel.IsHitTestVisible = true;
            _touchpadHardwareGate.Release();
        }
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

    private async Task ShowTouchpadErrorAsync(string message)
    {
        await ActionEditorDialogService.ShowMessageAsync(
            Content.XamlRoot,
            ResourceStringService.GetString("Touchpad.SettingsFailed.Title", "Touchpad settings failed"),
            message);
    }

    private void OnTouchpadLivePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ScheduleTouchpadStateRefresh();
    }

    private void OnTouchpadContactsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScheduleTouchpadStateRefresh();
    }

    private void RefreshVisibleContacts()
    {
        if (!Controller.ServiceRunning || Controller.TouchpadLive.IsVisualizerEmpty)
        {
            VisibleContacts.Clear();
            _smoothedContacts.Clear();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var activeSlots = new HashSet<int>();
        var orderedContacts = Controller.TouchpadLive.Contacts.ToList();
        for (var index = 0; index < orderedContacts.Count; index++)
        {
            var contact = orderedContacts[index];
            activeSlots.Add(contact.SlotIndex);

            var smoothed = GetSmoothedContact(contact, now);
            var snapshot = new TouchpadLiveContactSnapshot
            {
                SlotIndex = contact.SlotIndex,
                Tip = contact.Tip,
                Confidence = contact.Confidence,
                ContactId = contact.ContactId,
                X = (int)Math.Round(smoothed.X),
                Y = (int)Math.Round(smoothed.Y),
                Pressure = (int)Math.Round(smoothed.Pressure)
            };

            var viewModel = index < VisibleContacts.Count
                ? VisibleContacts[index]
                : new TouchpadLiveContactViewModel();
            viewModel.Update(snapshot);
            if (index >= VisibleContacts.Count)
            {
                VisibleContacts.Add(viewModel);
            }
        }

        while (VisibleContacts.Count > orderedContacts.Count)
        {
            VisibleContacts.RemoveAt(VisibleContacts.Count - 1);
        }

        foreach (var staleSlot in _smoothedContacts.Keys.Except(activeSlots).ToList())
        {
            _smoothedContacts.Remove(staleSlot);
        }
    }

    private SmoothedContactState GetSmoothedContact(TouchpadLiveContactViewModel contact, DateTimeOffset now)
    {
        if (!_smoothedContacts.TryGetValue(contact.SlotIndex, out var state))
        {
            state = new SmoothedContactState(contact.X, contact.Y, contact.Pressure, now);
            _smoothedContacts[contact.SlotIndex] = state;
            return state;
        }

        var elapsedMs = Math.Max(16d, (now - state.UpdatedAt).TotalMilliseconds);
        var blend = Math.Clamp(elapsedMs / 120d, 0.18d, 0.45d);

        state.X += (contact.X - state.X) * blend;
        state.Y += (contact.Y - state.Y) * blend;
        state.Pressure += (contact.Pressure - state.Pressure) * blend;
        state.UpdatedAt = now;
        _smoothedContacts[contact.SlotIndex] = state;
        return state;
    }

    private void RenderTouchpadVisualization()
    {
        _renderPending = false;

        var layoutWidth = TouchpadPreviewHost.ActualWidth;
        var layoutHeight = TouchpadPreviewHost.ActualHeight;
        if (layoutWidth <= 0 || layoutHeight <= 0)
        {
            return;
        }

        var state = Controller.TouchpadLive;
        TouchpadSurfaceCanvas.Children.Clear();
        TouchpadGuideCanvas.Children.Clear();
        TouchpadCanvas.Children.Clear();
        TouchpadPawImage.Visibility = Visibility.Collapsed;
        TouchpadCanvasEmptyStatePanel.Visibility = state.IsVisualizerEmpty ? Visibility.Visible : Visibility.Collapsed;
        if (state.IsVisualizerEmpty)
        {
            _displayPressure = 0;
            _lastDisplayPressureAt = default;
            _pressureDescriptionValue = 0;
            _lastPressureDescriptionAt = default;
            UpdatePressureDescriptionText(0);
            return;
        }

        var preview = TouchpadPreviewCoordinateSpace.Create(
            layoutWidth,
            layoutHeight,
            Controller.Touchpad.SurfaceWidth,
            Controller.Touchpad.SurfaceHeight);
        if (preview is null)
        {
            return;
        }

        TouchpadSurfaceCanvas.Width = layoutWidth;
        TouchpadSurfaceCanvas.Height = layoutHeight;
        TouchpadGuideCanvas.Width = layoutWidth;
        TouchpadGuideCanvas.Height = layoutHeight;
        TouchpadPawCanvas.Width = layoutWidth;
        TouchpadPawCanvas.Height = layoutHeight;
        TouchpadCanvas.Width = layoutWidth;
        TouchpadCanvas.Height = layoutHeight;

        var palette = GetTouchpadPalette();
        var displayPressure = GetDisplayedPressure(state);
        UpdatePressureDescriptionText(GetPressureDescriptionValue(state));

        var padShape = new ShapePath
        {
            Data = preview.CreatePadClipGeometry(),
            Fill = CreateBrush(palette.PadFill)
        };
        TouchpadSurfaceCanvas.Children.Add(padShape);

        AddEdgeSlideOverlays(preview, palette);
        AddCornerOverlays(preview, palette);

        var padOutline = new ShapePath
        {
            Data = preview.CreatePadClipGeometry(),
            Stroke = CreateBrush(palette.OverlayStroke),
            StrokeThickness = PreviewStrokeThickness
        };
        TouchpadGuideCanvas.Children.Add(padOutline);

        AddGridLines(preview, palette);
        if (Controller.ShowEasterEggs)
        {
            AddPawTexture(preview);
        }

        AddContacts(displayPressure, preview, palette);
    }

    private void AddPawTexture(TouchpadPreviewCoordinateSpace preview)
    {
        var width = Math.Min(preview.PadWidth * 0.52d, preview.PadHeight * 0.72d);
        var height = width * (150d / 180d);
        TouchpadPawImage.Width = width;
        TouchpadPawImage.Height = height;
        TouchpadPawImage.Opacity = App.ThemeService.GetResolvedTheme() == ElementTheme.Light ? 0.44d : 0.30d;
        TouchpadPawImage.Visibility = Visibility.Visible;

        Canvas.SetLeft(TouchpadPawImage, preview.PadLeft + ((preview.PadWidth - width) / 2d));
        Canvas.SetTop(TouchpadPawImage, preview.PadTop + ((preview.PadHeight - height) * 0.56d));
    }

    private async void UpdateTouchpadPawSource()
    {
        var theme = App.ThemeService.GetResolvedTheme();
        var fill = theme == ElementTheme.Light
            ? TouchpadPawLightFill
            : TouchpadPawDarkFill;

        var source = await SvgAssetTintService.CreateTintedImageSourceAsync("cat-paw.svg", fill);
        if (App.ThemeService.GetResolvedTheme() == theme)
        {
            TouchpadPawImage.Source = source;
        }
    }

    private void AddCornerOverlays(TouchpadPreviewCoordinateSpace preview, TouchpadVisualizerPalette palette)
    {
        var regions = new[]
        {
            (ViewModel: Controller.Touchpad.LeftTopCorner, Label: "LT"),
            (ViewModel: Controller.Touchpad.RightTopCorner, Label: "RT")
        };

        foreach (var region in regions)
        {
            var overlayInfo = preview.DescribeCorner(region.ViewModel.RegionId, region.ViewModel.Bounds);
            var overlay = new ShapePath
            {
                Data = overlayInfo.Geometry,
                Fill = CreateBrush(palette.OverlayFill)
            };
            TouchpadGuideCanvas.Children.Add(overlay);

            var stroke = new ShapePath
            {
                Data = overlayInfo.StrokeGeometry,
                Stroke = CreateBrush(palette.OverlayStroke),
                StrokeThickness = PreviewStrokeThickness
            };
            TouchpadGuideCanvas.Children.Add(stroke);

            var label = new TextBlock
            {
                Text = region.Label,
                Foreground = CreateBrush(palette.OverlayLabel),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            TouchpadGuideCanvas.Children.Add(label);
            label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));

            Canvas.SetLeft(label, overlayInfo.LabelCenter.X - (label.DesiredSize.Width / 2));
            Canvas.SetTop(label, overlayInfo.LabelCenter.Y - (label.DesiredSize.Height / 2));
        }
    }

    private void AddEdgeSlideOverlays(TouchpadPreviewCoordinateSpace preview, TouchpadVisualizerPalette palette)
    {
        var leftOverlay = preview.DescribeEdge(
            leftSide: true,
            Controller.Touchpad.LeftTopCorner.RegionId,
            Controller.Touchpad.LeftTopCorner.Bounds);
        AddEdgeOverlay(leftOverlay, "L", palette);

        var rightOverlay = preview.DescribeEdge(
            leftSide: false,
            Controller.Touchpad.RightTopCorner.RegionId,
            Controller.Touchpad.RightTopCorner.Bounds);
        AddEdgeOverlay(rightOverlay, "R", palette);
    }

    private void AddEdgeOverlay(TouchpadPreviewEdgeOverlay overlay, string labelText, TouchpadVisualizerPalette palette)
    {
        if (overlay.Height <= 1d || overlay.Width <= 1d)
        {
            return;
        }

        var shape = new ShapePath
        {
            Data = overlay.Geometry,
            Fill = CreateBrush(palette.OverlayFill)
        };
        TouchpadGuideCanvas.Children.Add(shape);

        var stroke = new ShapePath
        {
            Data = overlay.StrokeGeometry,
            Stroke = CreateBrush(palette.OverlayStroke),
            StrokeThickness = PreviewStrokeThickness
        };
        TouchpadGuideCanvas.Children.Add(stroke);

        var label = new TextBlock
        {
            Text = labelText,
            Foreground = CreateBrush(palette.OverlayLabel),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        };
        TouchpadGuideCanvas.Children.Add(label);
        label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, overlay.LabelCenter.X - (label.DesiredSize.Width / 2));
        Canvas.SetTop(label, overlay.LabelCenter.Y - (label.DesiredSize.Height / 2));
    }

    private void AddGridLines(TouchpadPreviewCoordinateSpace preview, TouchpadVisualizerPalette palette)
    {
        var horizontalInset = Math.Max(14d, preview.EdgeOverlayWidth + 10d);
        for (var index = 1; index < 4; index++)
        {
            var vertical = new Line
            {
                X1 = preview.PadLeft + (preview.PadWidth * index / 4d),
                Y1 = preview.PadTop + 14,
                X2 = preview.PadLeft + (preview.PadWidth * index / 4d),
                Y2 = preview.PadTop + preview.PadHeight - 14,
                Stroke = CreateBrush(palette.Grid),
                StrokeThickness = 1
            };
            TouchpadGuideCanvas.Children.Add(vertical);

            var horizontal = new Line
            {
                X1 = preview.PadLeft + horizontalInset,
                Y1 = preview.PadTop + (preview.PadHeight * index / 4d),
                X2 = preview.PadLeft + preview.PadWidth - horizontalInset,
                Y2 = preview.PadTop + (preview.PadHeight * index / 4d),
                Stroke = CreateBrush(palette.Grid),
                StrokeThickness = 1
            };
            TouchpadGuideCanvas.Children.Add(horizontal);
        }
    }

    private void AddContacts(double displayPressure, TouchpadPreviewCoordinateSpace preview, TouchpadVisualizerPalette palette)
    {
        var showHalo = displayPressure >= Controller.Touchpad.LightPressThreshold;
        var color = GetPressureColor(displayPressure, palette);
        foreach (var contact in VisibleContacts)
        {
            var point = preview.MapContact(contact.X, contact.Y);
            var ratio = Math.Clamp(contact.Pressure / PressureScaleMax, 0d, 1d);
            var radius = 9 + (ratio * 12);

            if (showHalo)
            {
                var halo = new Ellipse
                {
                    Width = radius * 2 + 14,
                    Height = radius * 2 + 14,
                    Fill = CreateBrush(ColorHelper.FromArgb(palette.ContactHaloAlpha, color.R, color.G, color.B))
                };
                TouchpadCanvas.Children.Add(halo);
                Canvas.SetLeft(halo, point.X - (halo.Width / 2));
                Canvas.SetTop(halo, point.Y - (halo.Height / 2));
            }

            var circle = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = CreateBrush(contact.Tip ? color : palette.ContactInactive),
                Stroke = CreateBrush(palette.ContactStroke),
                StrokeThickness = 1.5
            };
            TouchpadCanvas.Children.Add(circle);
            Canvas.SetLeft(circle, point.X - radius);
            Canvas.SetTop(circle, point.Y - radius);

            var label = new TextBlock
            {
                Text = contact.Label,
                Foreground = CreateBrush(palette.ContactLabel),
                FontWeight = FontWeights.SemiBold,
                FontSize = 11
            };
            TouchpadCanvas.Children.Add(label);
            label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, point.X - (label.DesiredSize.Width / 2));
            Canvas.SetTop(label, point.Y - (label.DesiredSize.Height / 2));
        }
    }

    private double GetDisplayedPressure(TouchpadLiveStateViewModel state)
    {
        var target = Math.Clamp(state.Pressure, 0d, PressureScaleMax);
        if (!state.HasInteraction && !state.ButtonPressed && target <= 0d)
        {
            _displayPressure = 0d;
            _lastDisplayPressureAt = state.Timestamp != default ? state.Timestamp : DateTimeOffset.UtcNow;
            return 0d;
        }

        var now = state.Timestamp != default ? state.Timestamp : DateTimeOffset.UtcNow;
        if (_lastDisplayPressureAt == default)
        {
            _displayPressure = target;
            _lastDisplayPressureAt = now;
            return _displayPressure;
        }

        var elapsedMs = Math.Max(16d, (now - _lastDisplayPressureAt).TotalMilliseconds);
        _lastDisplayPressureAt = now;

        var blend = Math.Clamp(elapsedMs / 140d, 0.18d, 0.52d);
        _displayPressure += (target - _displayPressure) * blend;

        if (Math.Abs(target - _displayPressure) < 1.5d || (!state.HasInteraction && target <= 0d && _displayPressure < 4d))
        {
            _displayPressure = target;
        }

        return _displayPressure;
    }

    private double GetPressureDescriptionValue(TouchpadLiveStateViewModel state)
    {
        var target = Math.Clamp(state.Pressure, 0d, PressureScaleMax);
        if (!state.HasInteraction && !state.ButtonPressed && target <= 0d)
        {
            _pressureDescriptionValue = 0d;
            _lastPressureDescriptionAt = state.Timestamp != default ? state.Timestamp : DateTimeOffset.UtcNow;
            return 0d;
        }

        var now = state.Timestamp != default ? state.Timestamp : DateTimeOffset.UtcNow;
        if (_lastPressureDescriptionAt == default)
        {
            _pressureDescriptionValue = target;
            _lastPressureDescriptionAt = now;
            return _pressureDescriptionValue;
        }

        var elapsedMs = Math.Max(16d, (now - _lastPressureDescriptionAt).TotalMilliseconds);
        _lastPressureDescriptionAt = now;

        var blend = Math.Clamp(elapsedMs / 220d, 0.08d, 0.18d);
        _pressureDescriptionValue += (target - _pressureDescriptionValue) * blend;

        if (Math.Abs(target - _pressureDescriptionValue) < 1.2d || (!state.HasInteraction && target <= 0d && _pressureDescriptionValue < 3d))
        {
            _pressureDescriptionValue = target;
        }

        return _pressureDescriptionValue;
    }

    private Windows.UI.Color GetPressureColor(double pressure, TouchpadVisualizerPalette palette)
    {
        pressure = Math.Clamp(pressure, 0d, PressureScaleMax);
        var lightPressThreshold = Math.Clamp(Controller.Touchpad.LightPressThreshold, 20, RuntimeDefaults.DefaultTouchpadDeepPressThreshold - 1);

        if (pressure <= 0d)
        {
            return palette.ContactInactive;
        }

        if (pressure <= lightPressThreshold)
        {
            return LerpColor(palette.ContactInactive, palette.PressureLight, pressure / lightPressThreshold);
        }

        if (pressure <= DeepPressThreshold)
        {
            return LerpColor(
                palette.PressureLight,
                palette.PressureMedium,
                (pressure - lightPressThreshold) / (DeepPressThreshold - lightPressThreshold));
        }

        if (pressure <= RedPressureThreshold)
        {
            return LerpColor(
                palette.PressureMedium,
                palette.PressureHigh,
                (pressure - DeepPressThreshold) / (RedPressureThreshold - DeepPressThreshold));
        }

        return palette.PressureHigh;
    }

    private static SolidColorBrush CreateBrush(Windows.UI.Color color) => new(color);

    private static TouchpadVisualizerPalette GetTouchpadPalette()
    {
        return App.ThemeService.GetResolvedTheme() == ElementTheme.Light
            ? new TouchpadVisualizerPalette(
                PadFill: ColorHelper.FromArgb(255, 232, 224, 211),
                OverlayFill: ColorHelper.FromArgb(255, 215, 202, 185),
                OverlayStroke: ColorHelper.FromArgb(255, 178, 162, 143),
                Grid: ColorHelper.FromArgb(255, 199, 185, 168),
                OverlayLabel: ColorHelper.FromArgb(220, 74, 67, 58),
                ContactInactive: ColorHelper.FromArgb(255, 118, 128, 142),
                ContactStroke: ColorHelper.FromArgb(255, 255, 252, 247),
                ContactLabel: Colors.White,
                PressureLight: ColorHelper.FromArgb(255, 38, 125, 196),
                PressureMedium: ColorHelper.FromArgb(255, 195, 103, 34),
                PressureHigh: ColorHelper.FromArgb(255, 210, 64, 64),
                ContactHaloAlpha: 42)
            : new TouchpadVisualizerPalette(
                PadFill: ColorHelper.FromArgb(255, 34, 39, 48),
                OverlayFill: ColorHelper.FromArgb(255, 46, 54, 66),
                OverlayStroke: ColorHelper.FromArgb(255, 88, 98, 116),
                Grid: ColorHelper.FromArgb(255, 54, 61, 74),
                OverlayLabel: ColorHelper.FromArgb(168, 255, 255, 255),
                ContactInactive: ColorHelper.FromArgb(255, 132, 140, 156),
                ContactStroke: Colors.White,
                ContactLabel: Colors.White,
                PressureLight: ColorHelper.FromArgb(255, 79, 170, 255),
                PressureMedium: ColorHelper.FromArgb(255, 255, 156, 84),
                PressureHigh: ColorHelper.FromArgb(255, 255, 96, 96),
                ContactHaloAlpha: 52);
    }

    private void UpdatePressureDescriptionText(double pressure)
    {
        TouchpadPressureTextBlock.Text = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            ResourceStringService.GetString("Touchpad.Pressure.Format", "Pressure: {0}"),
            (int)Math.Round(pressure));
    }

    private static Windows.UI.Color LerpColor(Windows.UI.Color from, Windows.UI.Color to, double t)
    {
        t = Math.Clamp(t, 0d, 1d);
        return ColorHelper.FromArgb(
            (byte)(from.A + ((to.A - from.A) * t)),
            (byte)(from.R + ((to.R - from.R) * t)),
            (byte)(from.G + ((to.G - from.G) * t)),
            (byte)(from.B + ((to.B - from.B) * t)));
    }

    private void RequestRender()
    {
        if (_renderPending)
        {
            return;
        }

        _renderPending = true;
        DispatcherQueue.TryEnqueue(RenderTouchpadVisualization);
    }

    private void ScheduleTouchpadStateRefresh()
    {
        if (_touchpadStateRefreshPending)
        {
            return;
        }

        _touchpadStateRefreshPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _touchpadStateRefreshPending = false;
            RefreshVisibleContacts();
            RequestRender();
        });
    }

    private async void TrySaveTouchpadConfigurationAsync()
    {
        if (Controller.IsReloadingConfiguration)
        {
            return;
        }

        try
        {
            Controller.SaveTouchpadConfiguration();
        }
        catch (Exception exception)
        {
            await ActionEditorDialogService.ShowMessageAsync(
                Content.XamlRoot,
                ResourceStringService.GetString("Mappings.Messages.SaveFailed.Title", "Could not save mapping"),
                exception.Message);
        }
    }

    private static TouchpadTriggerActionEditorViewModel? GetActionEditor(object sender)
    {
        return (sender as FrameworkElement)?.DataContext as TouchpadTriggerActionEditorViewModel;
    }

    private struct SmoothedContactState(double x, double y, double pressure, DateTimeOffset updatedAt)
    {
        public double X { get; set; } = x;
        public double Y { get; set; } = y;
        public double Pressure { get; set; } = pressure;
        public DateTimeOffset UpdatedAt { get; set; } = updatedAt;
    }

    private readonly record struct TouchpadVisualizerPalette(
        Windows.UI.Color PadFill,
        Windows.UI.Color OverlayFill,
        Windows.UI.Color OverlayStroke,
        Windows.UI.Color Grid,
        Windows.UI.Color OverlayLabel,
        Windows.UI.Color ContactInactive,
        Windows.UI.Color ContactStroke,
        Windows.UI.Color ContactLabel,
        Windows.UI.Color PressureLight,
        Windows.UI.Color PressureMedium,
        Windows.UI.Color PressureHigh,
        byte ContactHaloAlpha);
}
