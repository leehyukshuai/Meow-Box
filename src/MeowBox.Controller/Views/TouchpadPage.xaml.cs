using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using MeowBox.Controller.Services;
using MeowBox.Controller.ViewModels;
using MeowBox.Core.Models;
using Windows.Storage.Pickers;
using WinRT.Interop;
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;

namespace MeowBox.Controller.Views;

public sealed partial class TouchpadPage : Page
{
    private const double PressureScaleMax = 2500d;
    private const double DeepPressThreshold = RuntimeDefaults.DefaultTouchpadDeepPressThreshold;
    private const double RedPressureThreshold = 800d;
    private double _displayPressure;
    private DateTimeOffset _lastDisplayPressureAt;
    private double _pressureDescriptionValue;
    private DateTimeOffset _lastPressureDescriptionAt;
    private bool _renderPending;
    private bool _touchpadStateRefreshPending;
    private readonly Dictionary<int, SmoothedContactState> _smoothedContacts = [];

    public MeowBoxController Controller => App.Controller;

    public ObservableCollection<TouchpadLiveContactViewModel> VisibleContacts { get; } = [];

    public TouchpadPage()
    {
        InitializeComponent();
        XamlStringLocalizer.Apply(this);
        DataContext = Controller;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Controller.PropertyChanged += OnControllerPropertyChanged;
        SubscribeTouchpadState();
        SubscribeTouchpadActionEditors();
        ScheduleTouchpadStateRefresh();
        DispatcherQueue.TryEnqueue(() => XamlStringLocalizer.Apply(this));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Controller.PropertyChanged -= OnControllerPropertyChanged;
        UnsubscribeTouchpadState();
        UnsubscribeTouchpadActionEditors();
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

        if (e.PropertyName is nameof(ActionDefinitionViewModel.PrimaryKey) or
            nameof(ActionDefinitionViewModel.PrimaryKeyGroup) or
            nameof(ActionDefinitionViewModel.ModifierSelectionSignature))
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

        var dialog = new ActionPickerDialog(editor.Action.Type)
        {
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.SelectedAction is null)
        {
            return;
        }

        editor.Action.Type = dialog.SelectedAction.Key;
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
        var dialog = new AppPickerDialog(apps)
        {
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.SelectedApp is not null)
        {
            editor.Action.Target = dialog.SelectedApp.LaunchTarget;
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

        var path = await PickFileAsync([".exe", ".lnk", ".bat", "*"]);
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

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MeowBoxController.Touchpad) or nameof(MeowBoxController.TouchpadLightPressThreshold)))
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

            ScheduleTouchpadStateRefresh();
            RequestRender();
            XamlStringLocalizer.Apply(this);
        });
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
        TouchpadCanvas.Children.Clear();
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

        TouchpadCanvas.Width = preview.PadWidth;
        TouchpadCanvas.Height = preview.PadHeight;

        var displayPressure = GetDisplayedPressure(state);
        UpdatePressureDescriptionText(GetPressureDescriptionValue(state));

        var padBorder = new Border
        {
            Width = preview.PadWidth,
            Height = preview.PadHeight,
            CornerRadius = new CornerRadius(preview.PadCornerRadius),
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 34, 39, 48)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 88, 98, 116))
        };
        TouchpadCanvas.Children.Add(padBorder);
        Canvas.SetLeft(padBorder, preview.PadLeft);
        Canvas.SetTop(padBorder, preview.PadTop);

        AddCornerOverlays(preview);
        AddGridLines(preview);
        AddContacts(displayPressure, preview);
    }

    private void AddCornerOverlays(TouchpadPreviewCoordinateSpace preview)
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
                Fill = new SolidColorBrush(ColorHelper.FromArgb(34, 255, 255, 255)),
                Stroke = new SolidColorBrush(ColorHelper.FromArgb(255, 88, 98, 116)),
                StrokeThickness = 1.2
            };
            TouchpadCanvas.Children.Add(overlay);

            var label = new TextBlock
            {
                Text = region.Label,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(168, 255, 255, 255)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            TouchpadCanvas.Children.Add(label);
            label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));

            Canvas.SetLeft(label, overlayInfo.LabelCenter.X - (label.DesiredSize.Width / 2));
            Canvas.SetTop(label, overlayInfo.LabelCenter.Y - (label.DesiredSize.Height / 2));
        }
    }

    private void AddGridLines(TouchpadPreviewCoordinateSpace preview)
    {
        for (var index = 1; index < 4; index++)
        {
            var vertical = new Line
            {
                X1 = preview.PadLeft + (preview.PadWidth * index / 4d),
                Y1 = preview.PadTop + 14,
                X2 = preview.PadLeft + (preview.PadWidth * index / 4d),
                Y2 = preview.PadTop + preview.PadHeight - 14,
                Stroke = new SolidColorBrush(ColorHelper.FromArgb(255, 54, 61, 74)),
                StrokeThickness = 1
            };
            TouchpadCanvas.Children.Add(vertical);

            var horizontal = new Line
            {
                X1 = preview.PadLeft + 14,
                Y1 = preview.PadTop + (preview.PadHeight * index / 4d),
                X2 = preview.PadLeft + preview.PadWidth - 14,
                Y2 = preview.PadTop + (preview.PadHeight * index / 4d),
                Stroke = new SolidColorBrush(ColorHelper.FromArgb(255, 54, 61, 74)),
                StrokeThickness = 1
            };
            TouchpadCanvas.Children.Add(horizontal);
        }
    }

    private void AddContacts(double displayPressure, TouchpadPreviewCoordinateSpace preview)
    {
        var showHalo = displayPressure >= Controller.Touchpad.LightPressThreshold;
        var color = GetPressureColor(displayPressure);
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
                    Fill = new SolidColorBrush(ColorHelper.FromArgb(52, color.R, color.G, color.B))
                };
                TouchpadCanvas.Children.Add(halo);
                Canvas.SetLeft(halo, point.X - (halo.Width / 2));
                Canvas.SetTop(halo, point.Y - (halo.Height / 2));
            }

            var circle = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = new SolidColorBrush(contact.Tip ? color : ColorHelper.FromArgb(255, 132, 140, 156)),
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 1.5
            };
            TouchpadCanvas.Children.Add(circle);
            Canvas.SetLeft(circle, point.X - radius);
            Canvas.SetTop(circle, point.Y - radius);

            var label = new TextBlock
            {
                Text = contact.Label,
                Foreground = new SolidColorBrush(Colors.White),
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

    private Windows.UI.Color GetPressureColor(double pressure)
    {
        pressure = Math.Clamp(pressure, 0d, PressureScaleMax);
        var lightPressThreshold = Math.Clamp(Controller.Touchpad.LightPressThreshold, 20, RuntimeDefaults.DefaultTouchpadDeepPressThreshold - 1);
        var gray = ColorHelper.FromArgb(255, 132, 140, 156);
        var blue = ColorHelper.FromArgb(255, 79, 170, 255);
        var orange = ColorHelper.FromArgb(255, 255, 156, 84);
        var red = ColorHelper.FromArgb(255, 255, 96, 96);

        if (pressure <= 0d)
        {
            return gray;
        }

        if (pressure <= lightPressThreshold)
        {
            return LerpColor(gray, blue, pressure / lightPressThreshold);
        }

        if (pressure <= DeepPressThreshold)
        {
            return LerpColor(
                blue,
                orange,
                (pressure - lightPressThreshold) / (DeepPressThreshold - lightPressThreshold));
        }

        if (pressure <= RedPressureThreshold)
        {
            return LerpColor(
                orange,
                red,
                (pressure - DeepPressThreshold) / (RedPressureThreshold - DeepPressThreshold));
        }

        return red;
    }

    private void UpdatePressureDescriptionText(double pressure)
    {
        TouchpadPressureTextBlock.Text = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            LocalizedText.Pick("Pressure: {0}", "压力：{0}"),
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
            await ShowMessageAsync(Localizer.GetString("Mappings.Messages.SaveFailed.Title"), exception.Message);
        }
    }

    private async Task<string?> PickFileAsync(IEnumerable<string> types)
    {
        if (App.MainWindow is null)
        {
            return null;
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };

        foreach (var type in types)
        {
            picker.FileTypeFilter.Add(type);
        }

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = Localizer.GetString("Dialog.Close")
        };

        await dialog.ShowAsync();
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
}
