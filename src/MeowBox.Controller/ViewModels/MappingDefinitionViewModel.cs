using System.ComponentModel;
using Microsoft.UI.Xaml;
using MeowBox.Core.Models;
using MeowBox.Core.Services;

namespace MeowBox.Controller.ViewModels;

public sealed class MappingDefinitionViewModel : ObservableObject
{
    private readonly ActionDefinitionViewModel _action;
    private bool _enabled;
    private string _keyId;
    private string _keyDisplayName = ResourceStringService.GetString("Mapping.SelectKey", "Select key");

    public MappingDefinitionViewModel(KeyActionMappingConfiguration model)
    {
        Id = string.IsNullOrWhiteSpace(model.Id) ? Guid.NewGuid().ToString("N") : model.Id;
        _enabled = model.Enabled;
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
                OnPropertyChanged(nameof(KeyDescription));
            }
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value))
            {
                OnPropertyChanged(nameof(Summary));
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

    public string ListTitle => KeyDisplayName;

    public string KeyDescription => HardwareKeyCatalog.GetDescription(KeyId);

    public string Summary => BuildSummary();

    public string ActionIconGlyph => MappingDisplayCatalog.GetIconGlyph(Action.Type);

    public void UpdateDisplay(string keyDisplayName)
    {
        KeyDisplayName = keyDisplayName;
    }

    public KeyActionMappingConfiguration ToConfiguration()
    {
        return new KeyActionMappingConfiguration
        {
            Id = Id,
            Name = KeyDisplayName,
            Enabled = Enabled,
            KeyId = KeyId,
            Action = Action.ToConfiguration()
        };
    }

    private string BuildSummary()
    {
        if (ActionCatalog.SupportsOsd(Action.Type) && Action.HasAssignedAction)
        {
            return string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                ResourceStringService.GetString("Mapping.Summary.WithOsd", "{0} Also shows an OSD."),
                Action.ActionDescription);
        }

        if (!Enabled)
        {
            return ResourceStringService.GetString("Mapping.Summary.Disabled", "This mapping is disabled.");
        }

        return Action.ActionDescription;
    }

    private void OnActionChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ActionIconGlyph));
    }
}
