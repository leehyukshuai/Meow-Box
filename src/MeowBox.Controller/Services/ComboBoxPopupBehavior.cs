using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MeowBox.Controller.Services;

public static class ComboBoxPopupBehavior
{
    public static readonly DependencyProperty RoundedCornersProperty = DependencyProperty.RegisterAttached(
        "RoundedCorners", typeof(bool), typeof(ComboBoxPopupBehavior),
        new PropertyMetadata(false, OnRoundedCornersChanged));

    public static bool GetRoundedCorners(DependencyObject element) => (bool)element.GetValue(RoundedCornersProperty);

    public static void SetRoundedCorners(DependencyObject element, bool value) => element.SetValue(RoundedCornersProperty, value);

    private static void OnRoundedCornersChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is ComboBox comboBox)
        {
            comboBox.DropDownOpened -= OnDropDownOpened;
            if ((bool)args.NewValue)
            {
                comboBox.DropDownOpened += OnDropDownOpened;
            }
        }
    }

    private static void OnDropDownOpened(object? sender, object args)
    {
        if (sender is not ComboBox comboBox)
        {
            return;
        }

        // Desktop ComboBox popup placement can reset the native template's radius to zero.
        comboBox.DispatcherQueue.TryEnqueue(() =>
        {
            if (comboBox.IsDropDownOpen && VisualTreeHelper.GetChildrenCount(comboBox) > 0 &&
                VisualTreeHelper.GetChild(comboBox, 0) is FrameworkElement root &&
                root.FindName("PopupBorder") is Border border)
            {
                border.CornerRadius = new CornerRadius(8);
            }
        });
    }
}
