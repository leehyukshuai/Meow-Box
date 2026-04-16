using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using FnMappingTool.Controller.Services;
using FnMappingTool.Controller.ViewModels;
using FnMappingTool.Core.Models;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FnMappingTool.Controller.Views;

public sealed partial class TouchpadPage : Page
{
    private const int LightPressThreshold = 125;
    private const double PressureScaleMax = 2500d;
    private double _displayPressure;
    private DateTimeOffset _lastDisplayPressureAt;
    private bool _renderPending;
    private bool _touchpadStateRefreshPending;
    private bool _touchpadSurfaceExpanded = true;
    private readonly Dictionary<int, SmoothedContactState> _smoothedContacts = [];

    public FnMappingToolController Controller => App.Controller;

    public ObservableCollection<TouchpadLiveContactViewModel> VisibleContacts { get; } = [];
    public string SurfaceToggleLabel => _touchpadSurfaceExpanded ? "Collapse" : "Expand";
    public string SurfaceToggleGlyph => _touchpadSurfaceExpanded ? "\uE70D" : "\uE70E";
    public Visibility SurfaceContentVisibility => _touchpadSurfaceExpanded ? Visibility.Visible : Visibility.Collapsed;

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
        ScheduleTouchpadStateRefresh();
        DispatcherQueue.TryEnqueue(() => XamlStringLocalizer.Apply(this));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Controller.PropertyChanged -= OnControllerPropertyChanged;
        UnsubscribeTouchpadState();
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
        editor.RefreshStandardKeys();
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
        editor.RefreshStandardKeys();
        TrySaveTouchpadConfigurationAsync();
    }

    private void OnTouchpadEditorLostFocus(object sender, RoutedEventArgs e)
    {
        if (!Controller.IsReloadingConfiguration)
        {
            TrySaveTouchpadConfigurationAsync();
        }
    }

    private void OnTouchpadActionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Controller.IsReloadingConfiguration)
        {
            return;
        }

        var editor = GetActionEditor(sender);
        if (editor is null || editor.IsRefreshingStandardKeys)
        {
            return;
        }

        if (sender is ComboBox comboBox)
        {
            var selectedOption = e.AddedItems.OfType<StandardKeyOption>().FirstOrDefault()
                ?? comboBox.SelectedItem as StandardKeyOption;
            editor.Action.StandardKey = selectedOption?.Key ?? string.Empty;
        }

        TrySaveTouchpadConfigurationAsync();
    }

    private void OnTouchpadStandardKeyGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Controller.IsReloadingConfiguration)
        {
            return;
        }

        var editor = GetActionEditor(sender);
        if (editor is null)
        {
            return;
        }

        if (sender is ComboBox comboBox)
        {
            var selectedGroup = e.AddedItems.OfType<StandardKeyGroupOption>().FirstOrDefault()
                ?? comboBox.SelectedItem as StandardKeyGroupOption;
            editor.Action.StandardKeyGroup =
                selectedGroup?.Key ?? StandardKeyCatalog.GroupOptions[0].Key;
        }

        editor.RefreshStandardKeys();
        TrySaveTouchpadConfigurationAsync();
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

    private void OnTouchpadSurfaceToggleClick(object sender, RoutedEventArgs e)
    {
        _touchpadSurfaceExpanded = !_touchpadSurfaceExpanded;
        Bindings.Update();

        if (_touchpadSurfaceExpanded)
        {
            RequestRender();
        }
        else
        {
            TouchpadCanvas.Children.Clear();
        }
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FnMappingToolController.Touchpad))
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
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

        var layoutWidth = TouchpadVisualizerLayoutGrid.ActualWidth;
        var layoutHeight = TouchpadCanvas.ActualHeight;
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
            return;
        }

        var detailsMinWidth = 280d;
        var availablePadWidth = Math.Max(220d, layoutWidth - detailsMinWidth - 16d);
        var availablePadHeight = layoutHeight;
        var targetAspect = Controller.Touchpad.SurfaceWidth / (double)Math.Max(1, Controller.Touchpad.SurfaceHeight);

        var padWidth = Math.Min(availablePadWidth, availablePadHeight * targetAspect);
        var padHeight = padWidth / targetAspect;

        TouchpadCanvas.Width = padWidth;
        TouchpadCanvas.Height = padHeight;

        var padLeft = 0d;
        var padTop = 0d;
        var displayPressure = GetDisplayedPressure(state);
        var touchColor = GetPressureColor(displayPressure);

        var padBorder = new Border
        {
            Width = padWidth,
            Height = padHeight,
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 34, 39, 48)),
            BorderThickness = new Thickness(displayPressure >= LightPressThreshold ? 2 : 1),
            BorderBrush = new SolidColorBrush(displayPressure > 0
                ? ColorHelper.FromArgb(255, touchColor.R, touchColor.G, touchColor.B)
                : ColorHelper.FromArgb(255, 88, 98, 116))
        };
        TouchpadCanvas.Children.Add(padBorder);
        Canvas.SetLeft(padBorder, padLeft);
        Canvas.SetTop(padBorder, padTop);

        AddCornerOverlays(padLeft, padTop, padWidth, padHeight);
        AddGridLines(padLeft, padTop, padWidth, padHeight);
        AddContacts(displayPressure, padLeft, padTop, padWidth, padHeight);
    }

    private void AddCornerOverlays(double padLeft, double padTop, double padWidth, double padHeight)
    {
        var regions = new[]
        {
            (ViewModel: Controller.Touchpad.LeftTopCorner, Label: "LT"),
            (ViewModel: Controller.Touchpad.RightTopCorner, Label: "RT")
        };

        foreach (var region in regions)
        {
            var widthRatio = Math.Clamp((region.ViewModel.Bounds.Right - region.ViewModel.Bounds.Left) / (double)Math.Max(1, Controller.Touchpad.SurfaceWidth), 0d, 1d);
            var heightRatio = Math.Clamp((region.ViewModel.Bounds.Bottom - region.ViewModel.Bounds.Top) / (double)Math.Max(1, Controller.Touchpad.SurfaceHeight), 0d, 1d);
            var leftRatio = Math.Clamp(region.ViewModel.Bounds.Left / (double)Math.Max(1, Controller.Touchpad.SurfaceWidth), 0d, 1d);
            var topRatio = Math.Clamp(region.ViewModel.Bounds.Top / (double)Math.Max(1, Controller.Touchpad.SurfaceHeight), 0d, 1d);

            var overlay = new Border
            {
                Width = Math.Max(32d, padWidth * widthRatio),
                Height = Math.Max(28d, padHeight * heightRatio),
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(ColorHelper.FromArgb(18, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            };
            TouchpadCanvas.Children.Add(overlay);
            Canvas.SetLeft(overlay, padLeft + (padWidth * leftRatio));
            Canvas.SetTop(overlay, padTop + (padHeight * topRatio));

            var label = new TextBlock
            {
                Text = region.Label,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(168, 255, 255, 255)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            TouchpadCanvas.Children.Add(label);
            Canvas.SetLeft(label, padLeft + (padWidth * leftRatio) + 10);
            Canvas.SetTop(label, padTop + (padHeight * topRatio) + 8);
        }
    }

    private void AddGridLines(double padLeft, double padTop, double padWidth, double padHeight)
    {
        for (var index = 1; index < 4; index++)
        {
            var vertical = new Line
            {
                X1 = padLeft + (padWidth * index / 4d),
                Y1 = padTop + 14,
                X2 = padLeft + (padWidth * index / 4d),
                Y2 = padTop + padHeight - 14,
                Stroke = new SolidColorBrush(ColorHelper.FromArgb(255, 54, 61, 74)),
                StrokeThickness = 1
            };
            TouchpadCanvas.Children.Add(vertical);

            var horizontal = new Line
            {
                X1 = padLeft + 14,
                Y1 = padTop + (padHeight * index / 4d),
                X2 = padLeft + padWidth - 14,
                Y2 = padTop + (padHeight * index / 4d),
                Stroke = new SolidColorBrush(ColorHelper.FromArgb(255, 54, 61, 74)),
                StrokeThickness = 1
            };
            TouchpadCanvas.Children.Add(horizontal);
        }
    }

    private void AddContacts(double displayPressure, double padLeft, double padTop, double padWidth, double padHeight)
    {
        var showHalo = displayPressure >= LightPressThreshold;
        var color = GetPressureColor(displayPressure);
        var surfaceWidth = Math.Max(1d, Controller.Touchpad.SurfaceWidth);
        var surfaceHeight = Math.Max(1d, Controller.Touchpad.SurfaceHeight);
        foreach (var contact in VisibleContacts)
        {
            var x = padLeft + ((contact.X / surfaceWidth) * (padWidth - 24)) + 12;
            var y = padTop + ((contact.Y / surfaceHeight) * (padHeight - 24)) + 12;
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
                Canvas.SetLeft(halo, x - (halo.Width / 2));
                Canvas.SetTop(halo, y - (halo.Height / 2));
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
            Canvas.SetLeft(circle, x - radius);
            Canvas.SetTop(circle, y - radius);

            var label = new TextBlock
            {
                Text = contact.Label,
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.SemiBold,
                FontSize = 11
            };
            TouchpadCanvas.Children.Add(label);
            label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, x - (label.DesiredSize.Width / 2));
            Canvas.SetTop(label, y - (label.DesiredSize.Height / 2));
        }
    }

    private double GetDisplayedPressure(TouchpadLiveStateViewModel state)
    {
        var target = Math.Clamp(state.Pressure, 0d, PressureScaleMax);
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

    private static Windows.UI.Color GetPressureColor(double pressure)
    {
        pressure = Math.Clamp(pressure, 0d, PressureScaleMax);
        var blue = ColorHelper.FromArgb(255, 79, 170, 255);
        var yellow = ColorHelper.FromArgb(255, 246, 204, 92);
        var orange = ColorHelper.FromArgb(255, 255, 156, 84);
        var red = ColorHelper.FromArgb(255, 255, 96, 96);
        var purple = ColorHelper.FromArgb(255, 160, 110, 255);

        return pressure switch
        {
            <= LightPressThreshold => LerpColor(blue, yellow, pressure / LightPressThreshold),
            <= RuntimeDefaults.DefaultTouchpadDeepPressThreshold => LerpColor(
                orange,
                red,
                (pressure - LightPressThreshold) / (RuntimeDefaults.DefaultTouchpadDeepPressThreshold - LightPressThreshold)),
            _ => LerpColor(
                red,
                purple,
                (pressure - RuntimeDefaults.DefaultTouchpadDeepPressThreshold) / (PressureScaleMax - RuntimeDefaults.DefaultTouchpadDeepPressThreshold))
        };
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
