using System.Globalization;
using FnMappingTool.Core.Models;

namespace FnMappingTool.Controller.ViewModels;

public sealed class TouchpadConfigurationViewModel : ObservableObject
{
    private bool _enabled;
    private int _deepPressThreshold;
    private int _longPressDurationMs;
    private TouchpadTriggerActionEditorViewModel? _selectedActionEditor;

    public TouchpadConfigurationViewModel(TouchpadConfiguration? model = null)
    {
        model ??= new TouchpadConfiguration();
        _enabled = model.Enabled;
        _deepPressThreshold = model.DeepPressThreshold <= 0
            ? RuntimeDefaults.DefaultTouchpadDeepPressThreshold
            : model.DeepPressThreshold;
        _longPressDurationMs = model.LongPressDurationMs <= 0
            ? RuntimeDefaults.DefaultTouchpadCornerLongPressDurationMs
            : model.LongPressDurationMs;

        SurfaceWidth = model.SurfaceWidth > 0
            ? model.SurfaceWidth
            : RuntimeDefaults.DefaultTouchpadSurfaceWidth;
        SurfaceHeight = model.SurfaceHeight > 0
            ? model.SurfaceHeight
            : RuntimeDefaults.DefaultTouchpadSurfaceHeight;

        GlobalDeepPress = new TouchpadTriggerActionEditorViewModel(
            LocalizedText.Pick("Global deep press", "全局重按"),
            LocalizedText.Pick(
                "Runs once when the touchpad reaches the built-in deep press level, as long as the touch did not already match a corner override.",
                "当触控板达到内置重按力度时执行一次；如果当前触控已经命中角落重按，则会优先执行角落动作。"),
            [
                LocalizedText.Pick(
                    "Assign one custom action for a deep press anywhere on the touchpad.",
                    "你可以为触控板任意位置的重按分配一个自定义动作。"),
                LocalizedText.Pick(
                    "LT and RT can each provide their own deep-press and long-press actions.",
                    "LT 和 RT 区域都可以分别设置独立的重按和长按动作。"),
                LocalizedText.Pick(
                    "If the touch begins in LT or RT and reaches deep press there, the corner action overrides this global one.",
                    "如果触控从 LT 或 RT 开始并在该区域内达到重按，则会优先触发角落动作而不是这里的全局动作。"),
                LocalizedText.Pick(
                    "Long-press duration is configured from the Settings page.",
                    "长按触发时长可以在设置页面中调整。")
            ],
            model.DeepPressAction);
        LeftTopCorner = new TouchpadCornerRegionViewModel(
            TouchpadCornerRegionId.LeftTop,
            model.LeftTopCorner,
            SurfaceWidth,
            SurfaceHeight);
        RightTopCorner = new TouchpadCornerRegionViewModel(
            TouchpadCornerRegionId.RightTop,
            model.RightTopCorner,
            SurfaceWidth,
            SurfaceHeight);

        CornerRegions = [LeftTopCorner, RightTopCorner];
        AllActionEditors =
        [
            GlobalDeepPress,
            LeftTopCorner.DeepPress,
            RightTopCorner.DeepPress,
            LeftTopCorner.LongPress,
            RightTopCorner.LongPress
        ];
        SelectedActionEditor = AllActionEditors.FirstOrDefault();

        foreach (var editor in AllActionEditors)
        {
            editor.Action.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasAnyAssignedAction));
            };
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public int DeepPressThreshold
    {
        get => _deepPressThreshold;
        set => SetProperty(ref _deepPressThreshold, Math.Clamp(value, 100, 4000));
    }

    public int LongPressDurationMs
    {
        get => _longPressDurationMs;
        set => SetProperty(ref _longPressDurationMs, Math.Clamp(value, 200, 3000));
    }

    public TouchpadTriggerActionEditorViewModel? SelectedActionEditor
    {
        get => _selectedActionEditor;
        set => SetProperty(ref _selectedActionEditor, value);
    }

    public int SurfaceWidth { get; }

    public int SurfaceHeight { get; }

    public string SurfaceSizeLabel => string.Format(
        CultureInfo.CurrentCulture,
        "{0} × {1}",
        SurfaceWidth,
        SurfaceHeight);

    public string GlobalActionTitle => LocalizedText.Pick("Global action", "全局动作");

    public string GlobalActionDescription => LocalizedText.Pick(
        "Runs when a deep press lands outside the corner overrides shown above.",
        "当重按没有命中上方角落区域时，执行这里的全局动作。");

    public string GuidanceGlobalText => LocalizedText.Pick(
        "You can assign one custom action for a deep press anywhere on the touchpad.",
        "你可以为触控板的重按设置一个自定义动作。");

    public string GuidanceCornerText => LocalizedText.Pick(
        "LT and RT can each run separate deep-press and long-press actions.",
        "LT 和 RT 区域也可以分别设置重按和长按两种动作。");

    public string GuidanceOverrideText => LocalizedText.Pick(
        "Deep press in LT or RT overrides the global deep-press action.",
        "LT 和 RT 区域的重按会覆盖全局的重按触发动作。");

    public string GuidanceDurationText => LocalizedText.Pick(
        "Long-press timing can be adjusted from Settings.",
        "长按需要的触发时间可以在设置页面当中设置。");

    public bool HasAnyAssignedAction => AllActionEditors.Any(item => item.Action.HasAssignedAction);

    public TouchpadTriggerActionEditorViewModel GlobalDeepPress { get; }

    public ActionDefinitionViewModel DeepPressAction => GlobalDeepPress.Action;

    public TouchpadCornerRegionViewModel LeftTopCorner { get; }

    public TouchpadCornerRegionViewModel RightTopCorner { get; }

    public IReadOnlyList<TouchpadCornerRegionViewModel> CornerRegions { get; }

    public IReadOnlyList<TouchpadTriggerActionEditorViewModel> AllActionEditors { get; }

    public string ActionSummary => GlobalDeepPress.ActionSummary;

    public string ActionDescription => GlobalDeepPress.ActionDescription;

    public string ActionIconGlyph => GlobalDeepPress.ActionIconGlyph;

    public TouchpadConfiguration ToConfiguration()
    {
        return new TouchpadConfiguration
        {
            Enabled = HasAnyAssignedAction,
            DeepPressThreshold = RuntimeDefaults.DefaultTouchpadDeepPressThreshold,
            LongPressDurationMs = LongPressDurationMs,
            SurfaceWidth = SurfaceWidth,
            SurfaceHeight = SurfaceHeight,
            DeepPressAction = GlobalDeepPress.Action.ToConfiguration(),
            LeftTopCorner = LeftTopCorner.ToConfiguration(),
            RightTopCorner = RightTopCorner.ToConfiguration()
        };
    }
}
