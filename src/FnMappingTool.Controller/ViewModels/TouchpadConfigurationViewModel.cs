using FnMappingTool.Core.Models;

namespace FnMappingTool.Controller.ViewModels;

public sealed class TouchpadConfigurationViewModel : ObservableObject
{
    private readonly ActionDefinitionViewModel _deepPressAction;
    private bool _enabled;
    private int _deepPressThreshold;

    public TouchpadConfigurationViewModel(TouchpadConfiguration? model = null)
    {
        model ??= new TouchpadConfiguration();
        _enabled = model.Enabled;
        _deepPressThreshold = model.DeepPressThreshold <= 0
            ? RuntimeDefaults.DefaultTouchpadDeepPressThreshold
            : model.DeepPressThreshold;
        _deepPressAction = new ActionDefinitionViewModel(model.DeepPressAction);
        _deepPressAction.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ActionSummary));
            OnPropertyChanged(nameof(ActionDescription));
            OnPropertyChanged(nameof(ActionIconGlyph));
        };
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

    public ActionDefinitionViewModel DeepPressAction => _deepPressAction;

    public string ActionSummary => DeepPressAction.ActionLabel;

    public string ActionDescription => DeepPressAction.ActionDescription;

    public string ActionIconGlyph => DeepPressAction.ActionIconGlyph;

    public TouchpadConfiguration ToConfiguration()
    {
        return new TouchpadConfiguration
        {
            Enabled = DeepPressAction.HasAssignedAction,
            DeepPressThreshold = RuntimeDefaults.DefaultTouchpadDeepPressThreshold,
            DeepPressAction = DeepPressAction.ToConfiguration()
        };
    }
}
