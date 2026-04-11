using System.ComponentModel;
using FnMappingTool.Core.Models;

namespace FnMappingTool.Controller.ViewModels;

public sealed class MappingDefinitionViewModel : ObservableObject
{
    private readonly ActionDefinitionViewModel _action;
    private string _keyId;
    private string _keyDisplayName = LocalizedText.Pick("Select key", "选择按键");

    public MappingDefinitionViewModel(KeyActionMappingConfiguration model)
    {
        Id = string.IsNullOrWhiteSpace(model.Id) ? Guid.NewGuid().ToString("N") : model.Id;
        _keyId = model.KeyId ?? string.Empty;
        _action = new ActionDefinitionViewModel(model.Action);
        _action.PropertyChanged += OnActionChanged;
    }

    public string Id { get; }

    public string KeyId
    {
        get => _keyId;
        set
        {
            var normalizedValue = value ?? string.Empty;
            if (SetProperty(ref _keyId, normalizedValue))
            {
                OnPropertyChanged(nameof(ListTitle));
            }
        }
    }

    public ActionDefinitionViewModel Action => _action;

    public string KeyDisplayName
    {
        get => _keyDisplayName;
        private set
        {
            if (SetProperty(ref _keyDisplayName, value))
            {
                OnPropertyChanged(nameof(ListTitle));
            }
        }
    }

    public string ListTitle => $"{KeyDisplayName} -> {Action.ActionLabel}";

    public string Summary => Action.ActionDescription;

    public string ActionIconGlyph => Action.ActionIconGlyph;

    public void UpdateDisplay(string keyDisplayName)
    {
        KeyDisplayName = keyDisplayName;
    }

    public KeyActionMappingConfiguration ToConfiguration()
    {
        return new KeyActionMappingConfiguration
        {
            Id = Id,
            Name = ListTitle,
            Enabled = true,
            KeyId = KeyId,
            Action = Action.ToConfiguration()
        };
    }

    private void OnActionChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ListTitle));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ActionIconGlyph));
    }
}
