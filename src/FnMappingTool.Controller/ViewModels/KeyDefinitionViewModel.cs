using FnMappingTool.Core.Models;

namespace FnMappingTool.Controller.ViewModels;

public sealed class KeyDefinitionViewModel : ObservableObject
{
    private readonly EventMatcherConfiguration _trigger;
    private string _name;

    public KeyDefinitionViewModel(KeyDefinitionConfiguration model)
    {
        Id = string.IsNullOrWhiteSpace(model.Id) ? Guid.NewGuid().ToString("N") : model.Id;
        _name = model.Name ?? string.Empty;
        _trigger = model.Trigger ?? new EventMatcherConfiguration();
    }

    public string Id { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(ListTitle));
            }
        }
    }

    public string ListTitle => string.IsNullOrWhiteSpace(Name) ? "Unnamed key" : Name.Trim();

    public string TriggerDetails => _trigger.ToDisplayText();

    public KeyDefinitionConfiguration ToConfiguration()
    {
        return new KeyDefinitionConfiguration
        {
            Id = Id,
            Name = ListTitle,
            Trigger = _trigger
        };
    }
}
