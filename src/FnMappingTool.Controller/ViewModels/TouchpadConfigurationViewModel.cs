using FnMappingTool.Core.Models;

namespace FnMappingTool.Controller.ViewModels;

public sealed class TouchpadConfigurationViewModel : ObservableObject
{
    private bool _enabled;
    private int _lightPressThreshold;
    private int _deepPressThreshold;
    private int _longPressDurationMs;
    private TouchpadTriggerActionEditorViewModel? _selectedActionEditor;

    public TouchpadConfigurationViewModel(TouchpadConfiguration? model = null)
    {
        model ??= new TouchpadConfiguration();
        _enabled = model.Enabled;
        _lightPressThreshold = model.LightPressThreshold <= 0
            ? RuntimeDefaults.DefaultTouchpadLightPressThreshold
            : model.LightPressThreshold;
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
            null,
            model.DeepPressAction);
        LeftTopCorner = new TouchpadCornerRegionViewModel(
            TouchpadCornerRegionId.LeftTop,
            model.LeftTopCorner);
        RightTopCorner = new TouchpadCornerRegionViewModel(
            TouchpadCornerRegionId.RightTop,
            model.RightTopCorner);
        GuidanceLines =
        [
            LocalizedText.Pick(
                "Assign one custom action for a deep press anywhere on the touchpad.",
                "你可以为触控板任意位置的重按分配一个自定义动作。"),
            LocalizedText.Pick(
                "LT and RT can each run separate deep-press and long-press actions.",
                "LT 和 RT 区域也可以分别设置重按和长按两种动作。"),
            LocalizedText.Pick(
                "Deep press in LT or RT overrides the global deep-press action.",
                "LT 和 RT 区域的重按会覆盖全局的重按触发动作。"),
            LocalizedText.Pick(
                "Long-press timing can be adjusted from Settings.",
                "长按需要的触发时间可以在设置页面当中设置。")
        ];

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

    public int LightPressThreshold
    {
        get => _lightPressThreshold;
        set => SetProperty(ref _lightPressThreshold, Math.Clamp(value, 20, RuntimeDefaults.DefaultTouchpadDeepPressThreshold - 1));
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

    public bool HasAnyAssignedAction => AllActionEditors.Any(item => item.Action.HasAssignedAction);

    public TouchpadTriggerActionEditorViewModel GlobalDeepPress { get; }

    public ActionDefinitionViewModel DeepPressAction => GlobalDeepPress.Action;

    public TouchpadCornerRegionViewModel LeftTopCorner { get; }

    public TouchpadCornerRegionViewModel RightTopCorner { get; }

    public IReadOnlyList<string> GuidanceLines { get; }

    public IReadOnlyList<TouchpadCornerRegionViewModel> CornerRegions { get; }

    public IReadOnlyList<TouchpadTriggerActionEditorViewModel> AllActionEditors { get; }

    public TouchpadConfiguration ToConfiguration()
    {
        return new TouchpadConfiguration
        {
            Enabled = HasAnyAssignedAction,
            LightPressThreshold = LightPressThreshold,
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
