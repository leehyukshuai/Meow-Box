using System.Globalization;
using FnMappingTool.Core.Models;

namespace FnMappingTool.Controller.ViewModels;

public sealed class TouchpadCornerRegionViewModel : ObservableObject
{
    public TouchpadCornerRegionViewModel(
        string regionId,
        TouchpadCornerRegionConfiguration? model)
    {
        model ??= regionId == TouchpadCornerRegionId.RightTop
            ? TouchpadCornerRegionConfiguration.CreateRightTopDefault()
            : TouchpadCornerRegionConfiguration.CreateLeftTopDefault();

        RegionId = regionId;
        Bounds = model.Bounds ?? new TouchpadRegionBoundsConfiguration();

        var isRightTop = string.Equals(regionId, TouchpadCornerRegionId.RightTop, StringComparison.OrdinalIgnoreCase);
        var regionLabel = isRightTop
            ? LocalizedText.Pick("Right top", "右上角")
            : LocalizedText.Pick("Left top", "左上角");
        Title = isRightTop
            ? LocalizedText.Pick("Right top corner", "右上角")
            : LocalizedText.Pick("Left top corner", "左上角");
        Description = LocalizedText.Pick(
            "A touch that starts inside this corner can run its own deep-press or long-press action.",
            "从这个角落开始的触控可以运行独立的重按或长按动作。");

        DeepPress = new TouchpadTriggerActionEditorViewModel(
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                LocalizedText.Pick("{0} · Deep press", "{0} · 重按"),
                regionLabel),
            LocalizedText.Pick(
                "Runs once when a touch that starts in this corner reaches the built-in deep press level.",
                "当从这个角落开始的触控达到内置重按力度时执行一次。"),
            [
                string.Format(
                    CultureInfo.CurrentCulture,
                    LocalizedText.Pick(
                        "Use this when you want {0} deep press to behave differently from the rest of the touchpad.",
                        "如果你希望 {0} 的重按和触控板其他区域不同，可以在这里单独设置。"),
                    regionLabel),
                LocalizedText.Pick(
                    "Corner deep press overrides the global deep-press action.",
                    "角落重按会优先覆盖全局重按动作。"),
                LocalizedText.Pick(
                    "Long-press duration is configured separately from Settings and does not affect deep press.",
                    "长按时长在设置页面中单独配置，不会影响这里的重按触发。")
            ],
            model.DeepPressAction);
        LongPress = new TouchpadTriggerActionEditorViewModel(
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                LocalizedText.Pick("{0} · Long press", "{0} · 长按"),
                regionLabel),
            LocalizedText.Pick(
                "Runs after a touch that starts in this corner keeps holding until the configured long-press duration is reached.",
                "当从这个角落开始的触控持续按住，直到达到已配置的长按时长后执行。"),
            [
                string.Format(
                    CultureInfo.CurrentCulture,
                    LocalizedText.Pick(
                        "Use this for a deliberate hold gesture in {0}, without needing to press all the way to deep press.",
                        "如果你希望 {0} 支持一个更稳妥的停留手势，而不是必须按到重按力度，可以设置这里的长按动作。"),
                    regionLabel),
                LocalizedText.Pick(
                    "Long press is independent from the global deep press and from the corner deep-press action.",
                    "长按与全局重按、角落重按彼此独立。"),
                LocalizedText.Pick(
                    "You can change the required hold duration from Settings at any time.",
                    "所需的按住时长可以随时在设置页面中调整。")
            ],
            model.LongPressAction);
    }

    public string RegionId { get; }

    public string Title { get; }

    public string Description { get; }

    public TouchpadRegionBoundsConfiguration Bounds { get; }

    public TouchpadTriggerActionEditorViewModel DeepPress { get; }

    public TouchpadTriggerActionEditorViewModel LongPress { get; }

    public TouchpadCornerRegionConfiguration ToConfiguration()
    {
        return new TouchpadCornerRegionConfiguration
        {
            Id = RegionId,
            Bounds = new TouchpadRegionBoundsConfiguration
            {
                Left = Bounds.Left,
                Top = Bounds.Top,
                Right = Bounds.Right,
                Bottom = Bounds.Bottom
            },
            DeepPressAction = DeepPress.Action.ToConfiguration(),
            LongPressAction = LongPress.Action.ToConfiguration()
        };
    }
}
