using MeowBox.Core.Models;

namespace MeowBox.Controller.ViewModels;

public sealed class MappingOsdViewModel : ObservableObject
{
    private bool _enabled;

    public MappingOsdViewModel(MappingOsdConfiguration? model = null)
    {
        model ??= new MappingOsdConfiguration();
        _enabled = model.Enabled;
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public MappingOsdConfiguration ToConfiguration()
    {
        return new MappingOsdConfiguration
        {
            Enabled = Enabled
        };
    }
}
