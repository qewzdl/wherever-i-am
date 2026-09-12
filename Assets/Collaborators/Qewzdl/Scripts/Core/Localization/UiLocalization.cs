using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

// Only the one type, because UIElements has a TextElement of its own and the
// whole namespace would make the name ambiguous everywhere below.
using FontAsset = UnityEngine.TextCore.Text.FontAsset;

// The language, applied to a document's tree.
//
// Static, and holding a service and a list, for the same reason UiPreferences
// is: this is not a setting a screen reads when it opens, it is the state of
// every open document at once, and a document can be built at any time by Unity
// without asking anybody. The alternative was handing the localization service
// to seven documents that otherwise have no use for it.
//
// The service itself stays in the global scope for everything that is not a
// tree - the chat's system messages, a result announced at the end of a match.
// One door for trees and one for objects, which is the arrangement the settings
// service and UiPreferences already have.
public static class UiLocalization
{
    // What each label said in English, because the translation cannot be
    // undone from itself. Once a button reads "Начать" there is no way back to
    // "Start" except having written it down, and without a way back switching
    // language would work exactly once.
    private static readonly Dictionary<TextElement, string> sources = new();
    private static readonly List<TextElement> expired = new();

    // Every tree that has asked to be translated, whether or not there was
    // anything to translate it with at the time.
    //
    // A screen can be built before the services are composed - a document that
    // binds from OnEnable runs in the Awake phase, and composition happens in
    // a Start. That screen used to hand its tree over, be told there was no
    // language yet, and never be asked again, because a document binds once.
    // It was the only screen in the game that did that, which is why it was
    // the only one that never changed language.
    private static readonly List<VisualElement> roots = new();
    private static readonly List<VisualElement> goneRoots = new();

    private static ILocalizationService service;

    /// <summary>
    /// Raised after every tree has been re-translated, for the words a screen
    /// writes itself.
    /// </summary>
    /// <remarks>
    /// <see cref="Apply"/> reaches what the markup wrote and nothing else, and
    /// a screen that fills a list or a hint from code has to write those again
    /// in the new language. This is when.
    /// </remarks>
    public static event Action Changed;

    public static void Use(ILocalizationService localization)
    {
        if (ReferenceEquals(service, localization))
            return;

        if (service != null)
            service.LocaleChanged -= Reapply;

        service = localization;

        if (service == null)
            return;

        service.LocaleChanged += Reapply;

        // Whatever was built before this moment, translated now. The English
        // each element started with is still what it is showing, because
        // without a service nothing rewrote it.
        ApplyToKnownRoots();
        Reapply();
    }

    public static void Release(ILocalizationService localization)
    {
        if (!ReferenceEquals(service, localization))
            return;

        if (service != null)
            service.LocaleChanged -= Reapply;

        service = null;
        sources.Clear();
        roots.Clear();
    }

    /// <summary>
    /// Every language the build ships with, or nothing before composition.
    /// </summary>
    public static IReadOnlyList<LocaleOption> AvailableLocales =>
        service != null ? service.AvailableLocales : Array.Empty<LocaleOption>();

    /// <summary>One sentence, translated. Safe before anything is composed.</summary>
    public static string Text(string english)
    {
        return service != null ? service.Translate(english) : english;
    }

    /// <summary>
    /// Every static word in a tree, translated in place.
    /// </summary>
    /// <remarks>
    /// Called by a document when it binds, before anything fills in the parts
    /// that change. Labels the code writes afterwards are translated by
    /// whoever writes them - this only reaches what the markup wrote.
    /// </remarks>
    public static void Apply(VisualElement root)
    {
        if (root == null)
            return;

        // Remembered before it is translated, so that a tree offered too early
        // is still a tree this knows about when the language arrives.
        if (!roots.Contains(root))
            roots.Add(root);

        if (service == null)
            return;

        Dress(root);
        root.Query<Label>().ForEach(Translate);
        root.Query<Button>().ForEach(Translate);
    }

    // Set on the root and inherited by everything under it, so one write
    // dresses a whole document - including the chat and the spectator layers,
    // which are panels of their own rather than anything under a .screen.
    //
    // A language with no face of its own clears the inline style, which since
    // the stylesheets stopped naming fonts means Unity's default. That is a
    // table somebody forgot to finish rather than a thing to fall back on, and
    // LocalizationTests is what stops it reaching a build.
    private static void Dress(VisualElement root)
    {
        FontAsset font = service != null ? service.Font : null;

        if (font != null)
            root.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
        else
            root.style.unityFontDefinition = StyleKeyword.Null;
    }

    private static void DressKnownRoots()
    {
        for (int i = 0; i < roots.Count; i++)
        {
            if (roots[i] != null)
                Dress(roots[i]);
        }
    }

    private static void ApplyToKnownRoots()
    {
        goneRoots.Clear();

        for (int i = 0; i < roots.Count; i++)
        {
            VisualElement root = roots[i];

            if (root == null)
            {
                goneRoots.Add(root);
                continue;
            }

            Dress(root);
            root.Query<Label>().ForEach(Translate);
            root.Query<Button>().ForEach(Translate);
        }

        for (int i = 0; i < goneRoots.Count; i++)
            roots.Remove(goneRoots[i]);

        goneRoots.Clear();
    }

    private static void Translate(TextElement element)
    {
        if (element == null)
            return;

        // Whatever a player typed, and whatever a list is currently showing,
        // are not sentences this game wrote. The markup never puts text on
        // those, so anything found inside one arrived at runtime.
        if (IsInsideInput(element))
            return;

        // The first time this element is seen, what it says is the English the
        // markup wrote. Every time after, the English is what was written down
        // then - reading it back off the element would read a translation.
        if (!sources.TryGetValue(element, out string english))
        {
            if (string.IsNullOrEmpty(element.text))
                return;

            english = element.text;
            sources[element] = english;
        }

        element.text = service.Translate(english);
    }

    // A language changed under screens that are already up, which is the
    // ordinary case: it is changed from one of them.
    private static void Reapply()
    {
        DressKnownRoots();
        expired.Clear();

        foreach (KeyValuePair<TextElement, string> pair in sources)
        {
            TextElement element = pair.Key;

            // A document that has gone leaves its labels behind here until the
            // next pass, which is the only time the list is read and so the
            // only time tidying it is worth anything.
            if (element == null || element.panel == null)
            {
                expired.Add(element);
                continue;
            }

            element.text = service.Translate(pair.Value);
        }

        for (int i = 0; i < expired.Count; i++)
            sources.Remove(expired[i]);

        expired.Clear();
        Changed?.Invoke();
    }

    private static bool IsInsideInput(VisualElement element)
    {
        for (VisualElement parent = element.parent;
             parent != null;
             parent = parent.parent)
        {
            if (parent is TextField || parent is DropdownField)
                return true;
        }

        return false;
    }
}
