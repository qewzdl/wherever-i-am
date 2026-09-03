using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// The three preferences that are about the interface as a whole rather than
// about any screen in it: how big it is, how big its text is, and whether it
// moves.
//
// Static, and holding a list, which is worth saying out loud. These are not
// three settings that a screen reads when it opens - they are the state of
// every open document at once, and a document can be built at any time by
// Unity without asking anybody. So a document says here I am, once, when it
// binds; the settings screen says here are the values, whenever they change;
// and this holds the two together. The alternative was handing the settings
// service to five documents that otherwise have no use for it, and having each
// one subscribe, unsubscribe and re-apply on its own.
//
// The panel is separate from the roots because it is one asset shared by every
// screen: scale is set once, classes are set per document.
public static class UiPreferences
{
    private const string TextSmallClass = "text--small";
    private const string TextLargeClass = "text--large";
    private const string TextLargestClass = "text--largest";
    private const string ReducedMotionClass = "motion--reduced";

    // The resolution the interface is drawn for. Read off the panel the first
    // time it is seen rather than written here, so that moving the design to
    // another reference size does not leave a second copy of it in code.
    private static Vector2Int baseReferenceResolution;
    private static PanelSettings knownPanel;

    // Weak in spirit if not in type: a document that goes away leaves its root
    // behind here until the next apply, which is the only time the list is
    // read and the only time it is worth the cost of tidying.
    private static readonly List<VisualElement> Roots = new();

    private static int textSize = 1;
    private static bool reducedMotion;

    // Called by a document when it binds its tree. Binding happens again every
    // time a document is switched off and on, so the same root arriving twice
    // is expected rather than a fault.
    public static void Attach(VisualElement root)
    {
        if (root == null || Roots.Contains(root))
            return;

        Roots.Add(root);
        ApplyTo(root);
    }

    public static void Detach(VisualElement root)
    {
        if (root != null)
            Roots.Remove(root);
    }

    public static void Apply(PanelSettings panel, GameSettingsData settings)
    {
        if (settings == null)
            return;

        textSize = Mathf.Clamp(settings.textSize, 0, GameSettingsData.TextSizeNames.Length - 1);
        reducedMotion = settings.reducedMotion;

        ApplyScale(panel, settings.uiScale);

        for (int i = Roots.Count - 1; i >= 0; i--)
        {
            VisualElement root = Roots[i];

            if (root == null)
            {
                Roots.RemoveAt(i);
                continue;
            }

            ApplyTo(root);
        }
    }

    // Scale is the reference resolution divided by it, because the panel is in
    // Scale With Screen Size and that mode ignores PanelSettings.scale
    // outright - it is the constant-pixel knob. Telling the panel the screen is
    // smaller than it is makes everything on it bigger, which is the whole of
    // what a scale factor means here.
    //
    // Always computed from the size read the first time, never from whatever
    // the panel currently says, so that applying twice does not scale twice.
    private static void ApplyScale(PanelSettings panel, float uiScale)
    {
        if (panel == null)
            return;

        if (!ReferenceEquals(panel, knownPanel))
        {
            knownPanel = panel;
            baseReferenceResolution = panel.referenceResolution;
        }

        float factor = Mathf.Clamp(
            uiScale,
            GameSettingsData.MinUiScale,
            GameSettingsData.MaxUiScale);

        panel.referenceResolution = new Vector2Int(
            Mathf.RoundToInt(baseReferenceResolution.x / factor),
            Mathf.RoundToInt(baseReferenceResolution.y / factor));
    }

    // Put back what was read. PanelSettings is an asset, not a scene object, so
    // in the editor a scale applied during play is a change to a file in the
    // project - and a designer who played once with the interface at 150% would
    // find it that size the next time they opened the game, with nothing in the
    // history to say why.
    public static void Restore()
    {
        if (knownPanel != null)
            knownPanel.referenceResolution = baseReferenceResolution;

        knownPanel = null;
    }

    private static void ApplyTo(VisualElement root)
    {
        root.EnableInClassList(TextSmallClass, textSize == 0);
        root.EnableInClassList(TextLargeClass, textSize == 2);
        root.EnableInClassList(TextLargestClass, textSize == 3);
        root.EnableInClassList(ReducedMotionClass, reducedMotion);
    }
}
