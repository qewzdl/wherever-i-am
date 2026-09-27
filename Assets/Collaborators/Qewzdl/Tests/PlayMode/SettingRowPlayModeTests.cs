using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// The settings screen is written as one SettingRow per setting, and
// SettingsDocument finds each control by the setting's name. A row whose
// attributes stopped reaching it would still draw - a label and an empty
// control - and the screen would quietly stop saving anything, so this reads
// the real screen and checks every row built what its markup asked for.
public sealed class SettingRowPlayModeTests
{
    private const string MarkupPath = "Assets/Collaborators/Qewzdl/UI/Screens/Settings.uxml";
    private const string PanelPath = "Assets/Collaborators/Qewzdl/UI/UiPanelSettings.asset";

    private GameObject host;
    private PanelSettings panel;

    [TearDown]
    public void TearDown()
    {
        if (host != null)
            Object.DestroyImmediate(host);

        if (panel != null)
            Object.DestroyImmediate(panel);
    }

    [UnityTest]
    public IEnumerator EveryRowBuildsTheControlItsSettingIsLookedUpBy()
    {
        panel = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath));
        host = new GameObject(nameof(SettingRowPlayModeTests));

        UIDocument document = host.AddComponent<UIDocument>();
        document.panelSettings = panel;
        document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MarkupPath);
        yield return null;

        VisualElement root = document.rootVisualElement;
        int rows = 0;

        root.Query<SettingRow>().ForEach(row =>
        {
            rows++;
            string where = $"'{row.LabelText}' ({row.Setting})";

            Assert.That(row.Setting, Is.Not.Empty, $"{where} names no setting.");
            Assert.That(row.Q<Label>(className: "field__label").text, Is.EqualTo(row.LabelText), $"{where} lost its label.");

            VisualElement control = row.Q<VisualElement>(row.Setting);
            Assert.That(control, Is.Not.Null, $"{where} has no control named after its setting.");
            Assert.That(control.ClassListContains("field__control"), Is.True, $"{where} control is not styled as one.");

            if (row.Control == SettingRow.Kind.Slider)
            {
                Assert.That(control, Is.InstanceOf<Slider>(), where);
                Assert.That(row.Q<Label>(row.Setting + "Value"), Is.Not.Null, $"{where} shows no value.");
            }
            else if (row.Control == SettingRow.Kind.Dropdown)
            {
                Assert.That(control, Is.InstanceOf<DropdownField>(), where);
            }
            else
            {
                Assert.That(control, Is.InstanceOf<Toggle>(), where);
            }
        });

        Assert.That(rows, Is.GreaterThan(20), "The settings screen has lost its rows.");

        // One of each, against what the markup says, so an attribute that
        // stopped arriving is caught even where the default happens to fit.
        Slider fieldOfView = root.Q<Slider>("FieldOfView");
        Assert.That(fieldOfView.lowValue, Is.EqualTo(60f));
        Assert.That(fieldOfView.highValue, Is.EqualTo(110f));
        Assert.That(root.Q<DropdownField>("Quality"), Is.Not.Null);
        Assert.That(root.Q<Toggle>("VerticalSync"), Is.Not.Null);
    }
}
