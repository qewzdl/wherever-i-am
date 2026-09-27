using UnityEngine.UIElements;

// One row of a settings page: what the setting is called, the control that
// sets it, and - for a slider - the value it is at. In markup it is one line
// instead of five:
//
//   <SettingRow setting="MasterVolume" label="Master" low-value="0" high-value="1" />
//   <SettingRow setting="Quality" label="Quality" control="Dropdown" />
//
// It builds exactly what the rows used to spell out by hand - a .field with a
// .field__label, the control as .field__control, and a .field__value after a
// slider - so the stylesheets, the sounds and the localisation see what they
// always saw. The control is named after the setting, and a slider's value
// after the setting plus "Value", which is how SettingsDocument finds them.
[UxmlElement]
public partial class SettingRow : VisualElement
{
    public enum Kind
    {
        Slider,
        Dropdown,
        Toggle
    }

    private readonly Label title = new();

    private VisualElement control;
    private Label valueLabel;

    private string setting = string.Empty;
    private Kind kind = Kind.Slider;
    private float lowValue;
    private float highValue = 1f;

    public SettingRow()
    {
        AddToClassList("field");
        title.AddToClassList("field__label");
        Add(title);
        Build();
    }

    [UxmlAttribute("label")]
    public string LabelText
    {
        get => title.text;
        set => title.text = value;
    }

    [UxmlAttribute]
    public string Setting
    {
        get => setting;
        set { setting = value ?? string.Empty; Build(); }
    }

    [UxmlAttribute]
    public Kind Control
    {
        get => kind;
        set { kind = value; Build(); }
    }

    [UxmlAttribute]
    public float LowValue
    {
        get => lowValue;
        set { lowValue = value; Build(); }
    }

    [UxmlAttribute]
    public float HighValue
    {
        get => highValue;
        set { highValue = value; Build(); }
    }

    // Rebuilt whole on every attribute rather than patched: attributes arrive
    // one at a time, in no promised order, once, while the tree is cloned.
    private void Build()
    {
        control?.RemoveFromHierarchy();
        valueLabel?.RemoveFromHierarchy();
        valueLabel = null;

        switch (kind)
        {
            case Kind.Dropdown:
                control = new DropdownField();
                break;

            case Kind.Toggle:
                control = new Toggle();
                control.AddToClassList("toggle");
                break;

            default:
                control = new Slider(lowValue, highValue);
                valueLabel = new Label { name = setting + "Value" };
                valueLabel.AddToClassList("field__value");
                break;
        }

        control.name = setting;
        control.AddToClassList("field__control");
        Add(control);

        if (valueLabel != null)
            Add(valueLabel);
    }
}
