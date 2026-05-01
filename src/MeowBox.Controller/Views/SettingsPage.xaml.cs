using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MeowBox.Controller.Services;
using MeowBox.Core.Models;
using MeowBox.Core.Services;
using Windows.System;

namespace MeowBox.Controller.Views;

public sealed partial class SettingsPage : Page
{
    private static readonly Uri GitHubRepositoryUri = new("https://github.com/leehyukshuai/Meow-Box");
    private static readonly Uri AfdianUri = new("https://ifdian.net/a/Willowbank-September");

    private bool _isLoading;

    public MeowBoxController Controller => App.Controller;

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
        if (e.PropertyName is nameof(MeowBoxController.ServiceState) or
            nameof(MeowBoxController.ServiceRunning) or
            nameof(MeowBoxController.LanguagePreference) or
            nameof(MeowBoxController.TrayIconEnabled) or
            nameof(MeowBoxController.EasterEggsActivated) or
            nameof(MeowBoxController.ShowEasterEggs) or
            nameof(MeowBoxController.OsdDurationMs) or
            nameof(MeowBoxController.OsdDisplayMode) or
            nameof(MeowBoxController.OsdBackgroundOpacityPercent) or
            nameof(MeowBoxController.OsdScalePercent))
        {
            DispatcherQueue.TryEnqueue(SyncState);
        }
    }

    private void SyncState()
    {
        _isLoading = true;
        SelectComboItem(ThemeComboBox, Controller.ThemePreference);
        SelectComboItem(LanguageComboBox, Controller.LanguagePreference);
        SelectComboItem(OsdDisplayModeComboBox, Controller.OsdDisplayMode);
        ServiceToggleSwitch.IsOn = Controller.ServiceState is WorkerServiceState.Running or WorkerServiceState.Starting or WorkerServiceState.Stopping;
        ServiceToggleSwitch.IsEnabled = Controller.ServiceState is not WorkerServiceState.Starting and not WorkerServiceState.Stopping;
        TrayIconToggleSwitch.IsOn = Controller.TrayIconEnabled;
        ShowEasterEggsToggleSwitch.IsOn = Controller.ShowEasterEggs;
        ActivationCodeTextBox.IsEnabled = !Controller.EasterEggsActivated;
        ActivateEasterEggsButton.IsEnabled = !Controller.EasterEggsActivated;
        EasterEggVisibilityRow.Visibility = Controller.EasterEggsActivated ? Visibility.Visible : Visibility.Collapsed;
        ActivationStatusTextBlock.Text = Controller.EasterEggsActivated
            ? ResourceStringService.GetString("ActivationStatusText.Activated", "Activated. You can choose whether to show cat decorations.")
            : ResourceStringService.GetString("ActivationStatusText.NotActivated", "Not activated yet. Donate on Afdian to get an activation code.");
        OsdDurationNumberBox.Value = Controller.OsdDurationMs;
        OsdBackgroundOpacityNumberBox.Value = Controller.OsdBackgroundOpacityPercent;
        OsdScaleNumberBox.Value = Controller.OsdScalePercent;
        ConfigPathTextBox.Text = Controller.ConfigPath;
        _isLoading = false;
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

    private async void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        if (LanguageComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string languagePreference)
        {
            return;
        }

        if (string.Equals(languagePreference, Controller.LanguagePreference, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Controller.SetLanguagePreference(languagePreference);
        await ShowLanguageRestartAsync();
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
                await ShowMessageAsync(
                    ResourceStringService.GetString("Settings.Messages.StartServiceFailed.Title", "Could not start service"),
                    ResourceStringService.GetString("Settings.Messages.StartServiceFailed.Body", "The background worker could not be started or did not respond."));
                SyncState();
            }
        }
        else
        {
            await Controller.StopWorkerServiceAsync();
            SyncState();
        }
    }

    private void OnTrayIconChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Controller.SetTrayIconEnabled(TrayIconToggleSwitch.IsOn);
    }

    private void OnShowEasterEggsChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Controller.SetEasterEggsVisible(ShowEasterEggsToggleSwitch.IsOn);
    }

    private async void OnOpenGitHubClick(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(GitHubRepositoryUri);
    }

    private async void OnOpenAfdianClick(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(AfdianUri);
    }

    private async void OnActivateEasterEggsClick(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        if (!Controller.TryActivateEasterEggs(ActivationCodeTextBox.Text))
        {
            await ShowMessageAsync(
                ResourceStringService.GetString("Settings.Messages.ActivationFailed.Title", "Activation failed"),
                ResourceStringService.GetString("Settings.Messages.ActivationFailed.Body", "The activation code is incorrect. Please copy the code from Afdian and try again."));
            return;
        }

        ActivationCodeTextBox.Text = string.Empty;
        SyncState();
        await ShowActivationSuccessAsync();
    }

    private void OnOpenConfigFolderClick(object sender, RoutedEventArgs e)
    {
        Controller.OpenConfigFolder();
    }

    private async void OnRestoreDefaultsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            RequestedTheme = App.ThemeService.GetResolvedTheme(),
            XamlRoot = Content.XamlRoot,
            Title = ResourceStringService.GetString("Settings.Messages.RestoreDefaults.Title", "Restore defaults?"),
            Content = ResourceStringService.GetString("Settings.Messages.RestoreDefaults.Body", "This will replace the current configuration with the default settings."),
            PrimaryButtonText = ResourceStringService.GetString("Dialog.RestoreDefaults", "Restore defaults"),
            CloseButtonText = ResourceStringService.GetString("Dialog.Cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            Controller.RestoreDefaults();
            SyncState();
        }
        catch (Exception exception)
        {
            await ShowMessageAsync(
                ResourceStringService.GetString("Settings.Messages.RestoreDefaultsFailed.Title", "Could not restore defaults"),
                exception.Message);
        }
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
            RequestedTheme = App.ThemeService.GetResolvedTheme(),
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = ResourceStringService.GetString("Dialog.Close", "Close")
        };

        await dialog.ShowAsync();
    }

    private async Task ShowActivationSuccessAsync()
    {
        var content = new Border
        {
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(18),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LayerFillColorAltBrush"],
            Child = new StackPanel
            {
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "🎉  🐾  🎊",
                        FontSize = 34,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextAlignment = TextAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = ResourceStringService.GetString("Settings.Messages.ActivationSuccess.Headline", "Easter eggs activated!"),
                        FontSize = 22,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.WrapWholeWords
                    },
                    new TextBlock
                    {
                        Text = ResourceStringService.GetString("Settings.Messages.ActivationSuccess.Body", "Thank you for supporting Meow Box. You can now choose whether to show cat decorations in Open source and support."),
                        Style = (Style)Application.Current.Resources["CardBodyTextBlockStyle"],
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.WrapWholeWords,
                        MaxWidth = 360
                    }
                }
            }
        };

        var dialog = new ContentDialog
        {
            RequestedTheme = App.ThemeService.GetResolvedTheme(),
            XamlRoot = Content.XamlRoot,
            Title = ResourceStringService.GetString("Settings.Messages.ActivationSuccess.Title", "Congratulations"),
            Content = content,
            CloseButtonText = ResourceStringService.GetString("Dialog.Close", "Close")
        };

        await dialog.ShowAsync();
    }

    private async Task ShowLanguageRestartAsync()
    {
        var dialog = new ContentDialog
        {
            RequestedTheme = App.ThemeService.GetResolvedTheme(),
            XamlRoot = Content.XamlRoot,
            Title = ResourceStringService.GetString("Settings.Messages.LanguageRestart.Title", "Restart required"),
            Content = ResourceStringService.GetString("Settings.Messages.LanguageRestart.Body", "Language changes will apply after restarting Meow Box."),
            PrimaryButtonText = ResourceStringService.GetString("Dialog.RestartNow", "Restart now"),
            CloseButtonText = ResourceStringService.GetString("Dialog.Later", "Later"),
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            App.Restart();
        }
    }
}
