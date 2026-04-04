using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using FnMappingTool.Controller.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FnMappingTool.Controller.Views;

public sealed partial class SettingsPage : Page
{
    private bool _isLoading;

    public FnMappingToolController Controller => App.Controller;

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = Controller;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Controller.PropertyChanged += OnControllerPropertyChanged;
        SyncState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Controller.PropertyChanged -= OnControllerPropertyChanged;
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FnMappingToolController.ServiceRunning) or
            nameof(FnMappingToolController.AutostartEnabled) or
            nameof(FnMappingToolController.TrayIconEnabled))
        {
            DispatcherQueue.TryEnqueue(SyncState);
        }
    }

    private void SyncState()
    {
        _isLoading = true;
        SelectComboItem(ThemeComboBox, Controller.ThemePreference);
        ServiceOnRadioButton.IsChecked = Controller.ServiceRunning;
        ServiceOffRadioButton.IsChecked = !Controller.ServiceRunning;
        AutostartOnRadioButton.IsChecked = Controller.AutostartEnabled;
        AutostartOffRadioButton.IsChecked = !Controller.AutostartEnabled;
        TrayIconOnRadioButton.IsChecked = Controller.TrayIconEnabled;
        TrayIconOffRadioButton.IsChecked = !Controller.TrayIconEnabled;
        ServiceStatusGlyph.Background = new SolidColorBrush(Controller.ServiceRunning ? Colors.SeaGreen : Colors.IndianRed);
        _isLoading = false;
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string theme)
        {
            Controller.ApplyThemePreference(theme);
        }
    }

    private async void OnServiceStateChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        if (ReferenceEquals(sender, ServiceOnRadioButton))
        {
            if (!await Controller.StartWorkerServiceAsync())
            {
                await ShowMessageAsync("Could not start service", "The background worker could not be started or did not respond.");
            }
        }
        else
        {
            await Controller.StopWorkerServiceAsync();
        }
    }

    private void OnAutostartChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Controller.SetAutostart(ReferenceEquals(sender, AutostartOnRadioButton));
    }

    private void OnTrayIconChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Controller.SetTrayIconEnabled(ReferenceEquals(sender, TrayIconOnRadioButton));
    }

    private async void OnImportConfigClick(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is null)
        {
            return;
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add(".json");
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            try
            {
                Controller.ImportConfiguration(file.Path);
                SyncState();
            }
            catch (Exception exception)
            {
                await ShowMessageAsync("Could not import configuration", exception.Message);
            }
        }
    }

    private async void OnLoadPresetClick(object sender, RoutedEventArgs e)
    {
        if (PresetComboBox.SelectedItem is PresetConfigurationEntry preset)
        {
            try
            {
                Controller.ImportConfiguration(preset.Path);
                SyncState();
            }
            catch (Exception exception)
            {
                await ShowMessageAsync("Could not load preset", exception.Message);
            }
        }
    }

    private void OnOpenConfigFolderClick(object sender, RoutedEventArgs e)
    {
        Controller.OpenFolder(Path.GetDirectoryName(Controller.ConfigPath) ?? Controller.ConfigPath);
    }

    private static void SelectComboItem(ComboBox comboBox, string value)
    {
        foreach (var candidate in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(candidate.Tag as string, value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = candidate;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "Close"
        };

        await dialog.ShowAsync();
    }
}
