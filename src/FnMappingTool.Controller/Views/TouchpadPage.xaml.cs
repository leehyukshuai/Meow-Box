using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Text;
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
    private double _maxObservedX = 2048;
    private double _maxObservedY = 2048;
    private double _displayPressure;
    private DateTimeOffset _lastDisplayPressureAt;
    private bool _refreshingStandardKeyChoices;
    private bool _renderPending;
    private bool _touchpadStateRefreshPending;
    private readonly Dictionary<int, SmoothedContactState> _smoothedContacts = [];

    public FnMappingToolController Controller => App.Controller;

    public IReadOnlyList<StandardKeyGroupOption> StandardKeyGroups { get; } = StandardKeyCatalog.GroupOptions;

    public ObservableCollection<StandardKeyOption> FilteredStandardKeys { get; } = [];
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
        RefreshStandardKeyChoices();
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
        Controller.Touchpad.DeepPressAction.PropertyChanged += OnTouchpadActionPropertyChanged;
    }

    private void UnsubscribeTouchpadState()
    {
        Controller.TouchpadLive.PropertyChanged -= OnTouchpadLivePropertyChanged;
        Controller.TouchpadLive.Contacts.CollectionChanged -= OnTouchpadContactsChanged;
        Controller.Touchpad.DeepPressAction.PropertyChanged -= OnTouchpadActionPropertyChanged;
    }

    private async void OnChooseTouchpadActionClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ActionPickerDialog(Controller.Touchpad.DeepPressAction.Type)
        {
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.SelectedAction is null)
        {
            return;
        }

        Controller.SetTouchpadActionType(dialog.SelectedAction.Key);
        RefreshStandardKeyChoices();
        TrySaveTouchpadConfigurationAsync();
    }

    private void OnClearTouchpadActionClick(object sender, RoutedEventArgs e)
    {
        Controller.ClearTouchpadAction();
        RefreshStandardKeyChoices();
        TrySaveTouchpadConfigurationAsync();
    }

    private void OnTouchpadEditorLostFocus(object sender, RoutedEventArgs e)
    {
        if (!Controller.IsReloadingConfiguration)
        {
            TrySaveTouchpadConfigurationAsync();
        }
    }

    private void OnTouchpadSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Controller.IsReloadingConfiguration)
        {
            return;
        }

        if (_refreshingStandardKeyChoices)
        {
            return;
        }

        if (ReferenceEquals(sender, TouchpadStandardKeyComboBox))
        {
            var selectedOption = e.AddedItems.OfType<StandardKeyOption>().FirstOrDefault()
                ?? TouchpadStandardKeyComboBox.SelectedItem as StandardKeyOption;

            Controller.Touchpad.DeepPressAction.StandardKey = selectedOption?.Key ?? string.Empty;
        }

        TrySaveTouchpadConfigurationAsync();
    }

    private void OnStandardKeyGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Controller.IsReloadingConfiguration)
        {
            return;
        }

        if (ReferenceEquals(sender, TouchpadStandardKeyGroupComboBox))
        {
            var selectedGroup = e.AddedItems.OfType<StandardKeyGroupOption>().FirstOrDefault()
                ?? TouchpadStandardKeyGroupComboBox.SelectedItem as StandardKeyGroupOption;

            Controller.Touchpad.DeepPressAction.StandardKeyGroup =
                selectedGroup?.Key ?? StandardKeyCatalog.GroupOptions[0].Key;
        }

        RefreshStandardKeyChoices();
        TrySaveTouchpadConfigurationAsync();
    }

    private async void OnPickInstalledAppClick(object sender, RoutedEventArgs e)
    {
        var apps = await Controller.GetInstalledAppsAsync();
        var dialog = new AppPickerDialog(apps)
        {
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.SelectedApp is not null)
        {
            Controller.Touchpad.DeepPressAction.Target = dialog.SelectedApp.LaunchTarget;
            TrySaveTouchpadConfigurationAsync();
        }
    }

    private async void OnBrowseExecutableClick(object sender, RoutedEventArgs e)
    {
        var path = await PickFileAsync([".exe", ".lnk", ".bat", "*"]);
        if (!string.IsNullOrWhiteSpace(path))
        {
            Controller.Touchpad.DeepPressAction.Target = path;
            TrySaveTouchpadConfigurationAsync();
        }
    }

    private void OnTouchpadCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RequestRender();
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FnMappingToolController.Touchpad))
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            UnsubscribeTouchpadState();
            SubscribeTouchpadState();
            RefreshStandardKeyChoices();
            ScheduleTouchpadStateRefresh();
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

    private void OnTouchpadActionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ActionDefinitionViewModel.Type))
        {
            DispatcherQueue.TryEnqueue(RefreshStandardKeyChoices);
        }
    }

    private void RefreshStandardKeyChoices()
    {
        _refreshingStandardKeyChoices = true;
        try
        {
            var selectedGroup = Controller.Touchpad.DeepPressAction.StandardKeyGroup;
            FilteredStandardKeys.Clear();
            foreach (var option in StandardKeyCatalog.All.Where(item => StandardKeyCatalog.MatchesGroup(item, selectedGroup)))
            {
                FilteredStandardKeys.Add(option);
            }

            TouchpadStandardKeyGroupComboBox.SelectedValue = selectedGroup;
            var selectedKey = Controller.Touchpad.DeepPressAction.StandardKey;
            TouchpadStandardKeyComboBox.SelectedItem = FilteredStandardKeys
                .FirstOrDefault(item => string.Equals(item.Key, selectedKey, StringComparison.OrdinalIgnoreCase));
            TouchpadStandardKeyComboBox.SelectedValue = selectedKey;
        }
        finally
        {
            _refreshingStandardKeyChoices = false;
        }
    }

    private void RefreshVisibleContacts()
    {
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

        foreach (var contact in VisibleContacts)
        {
            _maxObservedX = Math.Max(_maxObservedX, contact.X);
            _maxObservedY = Math.Max(_maxObservedY, contact.Y);
        }

        var detailsMinWidth = 240d;
        var availablePadWidth = Math.Max(220d, layoutWidth - detailsMinWidth - 16d);
        var availablePadHeight = layoutHeight;
        var targetAspect = 3d / 2d;

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

        AddGridLines(padLeft, padTop, padWidth, padHeight);
        AddContacts(displayPressure, padLeft, padTop, padWidth, padHeight);
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
        foreach (var contact in VisibleContacts)
        {
            var x = padLeft + ((contact.X / Math.Max(1d, _maxObservedX)) * (padWidth - 24)) + 12;
            var y = padTop + ((contact.Y / Math.Max(1d, _maxObservedY)) * (padHeight - 24)) + 12;
            var ratio = Math.Clamp(contact.Pressure / PressureScaleMax, 0d, 1d);
            var radius = 10 + (ratio * 16);

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

    private struct SmoothedContactState(double x, double y, double pressure, DateTimeOffset updatedAt)
    {
        public double X { get; set; } = x;
        public double Y { get; set; } = y;
        public double Pressure { get; set; } = pressure;
        public DateTimeOffset UpdatedAt { get; set; } = updatedAt;
    }
}
