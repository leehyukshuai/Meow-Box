using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FnMappingTool.Controller.Services;
using FnMappingTool.Core.Models;
namespace FnMappingTool.Controller.Views;

public sealed partial class SettingsPage : Page
{
    private bool _isLoading;

    public FnMappingToolController Controller => App.Controller;

    public ObservableCollection<ConfigurationFileEntry> ConfigFiles { get; } = [];

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
        RefreshConfigFiles();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Controller.PropertyChanged -= OnControllerPropertyChanged;
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FnMappingToolController.ServiceRunning) or
            nameof(FnMappingToolController.AutostartEnabled) or
            nameof(FnMappingToolController.TrayIconEnabled) or
            nameof(FnMappingToolController.OsdDurationMs) or
            nameof(FnMappingToolController.OsdDisplayMode) or
            nameof(FnMappingToolController.OsdBackgroundOpacityPercent) or
            nameof(FnMappingToolController.OsdScalePercent))
        {
            DispatcherQueue.TryEnqueue(SyncState);
        }
    }

    private void SyncState()
    {
        _isLoading = true;
        SelectComboItem(ThemeComboBox, Controller.ThemePreference);
        SelectComboItem(OsdDisplayModeComboBox, Controller.OsdDisplayMode);
        ServiceToggleSwitch.IsOn = Controller.ServiceRunning;
        AutostartToggleSwitch.IsOn = Controller.AutostartEnabled;
        TrayIconToggleSwitch.IsOn = Controller.TrayIconEnabled;
        OsdDurationNumberBox.Value = Controller.OsdDurationMs;
        OsdBackgroundOpacityNumberBox.Value = Controller.OsdBackgroundOpacityPercent;
        OsdScaleNumberBox.Value = Controller.OsdScalePercent;
        OsdIconFolderTextBox.Text = Controller.OsdIconDirectory;
        ConfigDirectoryTextBox.Text = Controller.ConfigDirectory;
        _isLoading = false;
    }

    private void RefreshConfigFiles(string? preferredPath = null)
    {
        var selectedPath = preferredPath ?? (ConfigFilesComboBox.SelectedItem as ConfigurationFileEntry)?.Path;
        var configDirectory = Controller.ConfigDirectory;
        Directory.CreateDirectory(configDirectory);

        _isLoading = true;
        ConfigFilesComboBox.SelectedItem = null;
        ConfigFilesComboBox.ItemsSource = null;
        ConfigFiles.Clear();

        foreach (var file in Directory.GetFiles(configDirectory, "*.json", SearchOption.TopDirectoryOnly)
                     .Where(file => !string.Equals(file, Controller.ConfigPath, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            ConfigFiles.Add(new ConfigurationFileEntry
            {
                DisplayName = Path.GetFileName(file),
                Path = file
            });
        }

        ConfigFilesComboBox.ItemsSource = ConfigFiles;
        ConfigFilesComboBox.SelectedItem = ConfigFiles.FirstOrDefault(item =>
            string.Equals(item.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
            ?? ConfigFiles.FirstOrDefault();
        _isLoading = false;
        UpdatePresetSelectionState();
    }

    private void ApplyOsdSettingsFromControls()
    {
        if (_isLoading)
        {
            return;
        }

        var displayMode = (OsdDisplayModeComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? OsdDisplayMode.IconOnly;
        Controller.ApplyOsdPreferences(
            (int)Math.Round(Math.Max(500, OsdDurationNumberBox.Value)),
            displayMode,
            (int)Math.Round(Math.Clamp(OsdBackgroundOpacityNumberBox.Value, 0, 100)),
            (int)Math.Round(Math.Clamp(OsdScaleNumberBox.Value, 60, 200)));
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

    private void OnOsdSettingChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyOsdSettingsFromControls();
    }

    private void OnOsdNumberValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        ApplyOsdSettingsFromControls();
    }

    private async void OnServiceStateChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        if (ServiceToggleSwitch.IsOn)
        {
            if (!await Controller.StartWorkerServiceAsync())
            {
                await ShowMessageAsync("Could not start service", "The background worker could not be started or did not respond.");
                SyncState();
            }
        }
        else
        {
            await Controller.StopWorkerServiceAsync();
            SyncState();
        }
    }

    private void OnAutostartChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Controller.SetAutostart(AutostartToggleSwitch.IsOn);
    }

    private void OnTrayIconChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Controller.SetTrayIconEnabled(TrayIconToggleSwitch.IsOn);
    }

    private async void OnImportSelectedConfigClick(object sender, RoutedEventArgs e)
    {
        if (ConfigFilesComboBox.SelectedItem is not ConfigurationFileEntry configurationFile)
        {
            return;
        }

        var presetPath = configurationFile.Path;
        try
        {
            Controller.ImportConfiguration(presetPath);
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("Could not import configuration", exception.Message);
            return;
        }

        RefreshConfigFiles(presetPath);
        await ShowImportAppliedAsync();
    }

    private void OnRefreshConfigFilesClick(object sender, RoutedEventArgs e)
    {
        RefreshConfigFiles();
    }

    private void OnConfigFileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        UpdatePresetSelectionState();
    }

    private void OnOpenOsdIconFolderClick(object sender, RoutedEventArgs e)
    {
        Controller.OpenOsdIconFolder();
    }

    private void OnRefreshOsdIconFolderClick(object sender, RoutedEventArgs e)
    {
        Controller.RefreshOsdIconCatalog();
        SyncState();
    }

    private void OnOpenConfigFolderClick(object sender, RoutedEventArgs e)
    {
        Controller.OpenFolder(Controller.ConfigDirectory);
    }

    private void UpdatePresetSelectionState()
    {
        LoadPresetButton.IsEnabled = ConfigFilesComboBox.SelectedItem is ConfigurationFileEntry;
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

    private async Task ShowImportAppliedAsync()
    {
        var serviceRunning = Controller.ServiceRunning;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Configuration imported",
            Content = serviceRunning
                ? "The file has been imported. Restart the service to apply it."
                : "The file has been imported. Start the service when you're ready to apply it.",
            CloseButtonText = "Later"
        };

        if (serviceRunning)
        {
            dialog.PrimaryButtonText = "Restart service";
            dialog.DefaultButton = ContentDialogButton.Primary;
        }

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (!await Controller.RestartWorkerServiceAsync())
        {
            await ShowMessageAsync("Could not restart service", "The background worker could not be restarted.");
            SyncState();
            return;
        }

        SyncState();
    }
}

public sealed class ConfigurationFileEntry
{
    public string DisplayName { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
}
