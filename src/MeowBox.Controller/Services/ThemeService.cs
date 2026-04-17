using Microsoft.UI.Xaml;
using MeowBox.Core.Models;

namespace MeowBox.Controller.Services;

public sealed class ThemeService
{
    private FrameworkElement? _root;

    public event EventHandler<ElementTheme>? ResolvedThemeChanged;

    public string CurrentPreference { get; private set; } = ThemePreference.System;

    public void Initialize(Window window, string preference)
    {
        if (_root is not null)
        {
            _root.ActualThemeChanged -= OnActualThemeChanged;
        }

        _root = window.Content as FrameworkElement;
        if (_root is not null)
        {
            _root.ActualThemeChanged += OnActualThemeChanged;
        }

        ApplyPreference(preference);
    }

    public void ApplyPreference(string preference)
    {
        CurrentPreference = preference switch
        {
            ThemePreference.Light => ThemePreference.Light,
            ThemePreference.Dark => ThemePreference.Dark,
            _ => ThemePreference.System
        };

        if (_root is not null)
        {
            _root.RequestedTheme = CurrentPreference switch
            {
                ThemePreference.Light => ElementTheme.Light,
                ThemePreference.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }

        ResolvedThemeChanged?.Invoke(this, GetResolvedTheme());
    }

    public ElementTheme GetResolvedTheme()
    {
        return _root?.ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        ResolvedThemeChanged?.Invoke(this, GetResolvedTheme());
    }
}
