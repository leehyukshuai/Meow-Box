using MeowBox.Core.Models;

namespace MeowBox.Controller.ViewModels;

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
            null,
            model.DeepPressAction);
        LongPress = new TouchpadTriggerActionEditorViewModel(
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                LocalizedText.Pick("{0} · Long press", "{0} · 长按"),
                regionLabel),
            LocalizedText.Pick(
                "Runs after a touch that starts in this corner keeps holding until the configured long-press duration is reached.",
                "当从这个角落开始的触控持续按住，直到达到已配置的长按时长后执行。"),
            null,
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
