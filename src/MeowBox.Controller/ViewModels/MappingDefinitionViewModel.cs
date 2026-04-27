using System.ComponentModel;
using Microsoft.UI.Xaml;
using MeowBox.Core.Models;

namespace MeowBox.Controller.ViewModels;

public sealed class MappingDefinitionViewModel : ObservableObject
{
    private readonly ActionDefinitionViewModel _action;
    private readonly MappingOsdViewModel _osd;
    private bool _enabled;
    private string _keyId;
    private string _keyDisplayName = LocalizedText.Pick("Select key", "选择按键");

    public MappingDefinitionViewModel(KeyActionMappingConfiguration model)
    {
        Id = string.IsNullOrWhiteSpace(model.Id) ? Guid.NewGuid().ToString("N") : model.Id;
        _enabled = model.Enabled;
        _keyId = model.KeyId ?? string.Empty;
        _action = new ActionDefinitionViewModel(model.Action);
        _action.PropertyChanged += OnActionChanged;
        _osd = new MappingOsdViewModel(model.Osd);
        _osd.PropertyChanged += OnOsdChanged;
        if (!SupportsConfigurableOsd && _osd.Enabled)
        {
            _osd.Enabled = false;
        }
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

    public MappingOsdViewModel Osd => _osd;

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

    public string ActionIconGlyph => MappingDisplayCatalog.GetIconGlyph(Action.Type, Osd.Enabled);

    public bool SupportsConfigurableOsd => BuiltInOsdCatalog.SupportsToggle(KeyId, Action.Type);

    public Visibility OsdVisibility => SupportsConfigurableOsd ? Visibility.Visible : Visibility.Collapsed;

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
            Action = Action.ToConfiguration(),
            Osd = Osd.ToConfiguration()
        };
    }

    private string BuildSummary()
    {
        if (SupportsConfigurableOsd && Osd.Enabled && Action.HasAssignedAction)
        {
            return string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                LocalizedText.Pick("{0} Also shows an OSD.", "{0} 还会显示 OSD。"),
                Action.ActionDescription);
        }

        if (SupportsConfigurableOsd && Osd.Enabled)
        {
            return LocalizedText.Pick("Shows an OSD.", "显示 OSD。");
        }

        if (!Enabled)
        {
            return LocalizedText.Pick("This mapping is disabled.", "这个映射已禁用。");
        }

        return Action.ActionDescription;
    }

    private void OnActionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!SupportsConfigurableOsd && Osd.Enabled)
        {
            Osd.Enabled = false;
        }

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ActionIconGlyph));
        OnPropertyChanged(nameof(SupportsConfigurableOsd));
        OnPropertyChanged(nameof(OsdVisibility));
    }

    private void OnOsdChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ActionIconGlyph));
    }
}
