using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;

// One language, as an asset.
//
// Kept the way every other tunable in this project is kept - a ScriptableObject
// beside the thing it describes - so that a translator's work is a file in the
// repository rather than a column in somebody's spreadsheet.
//
// The English table is the odd one out and worth explaining: its rows translate
// each sentence to itself. That looks like nothing, and it does nothing at
// runtime, but it is the catalogue. It is how anybody can see every word the
// game says without opening eight screens and nine components, and it is what
// a new language is copied from.
[CreateAssetMenu(
    fileName = "LocaleTable",
    menuName = "Wherever I Am/Localization/Locale Table")]
public sealed class LocaleTable : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        [TextArea] public string English;
        [TextArea] public string Translation;
    }

    [Tooltip("BCP 47, the way the rest of the world writes a language: en, " +
             "ru, pt-BR. Shown nowhere; used to pick this table.")]
    [SerializeField] private string locale = "en";

    [Tooltip("What the language is called in itself, for a language picker " +
             "that has not been built yet.")]
    [SerializeField] private string displayName = "English";

    // A translation nobody can read is not a translation. A face is drawn for
    // an alphabet: ask one drawn for English to spell Cyrillic and it has
    // nothing to give, because a dynamic atlas can only add a glyph the source
    // file actually has.
    //
    // So the face belongs to the language, and this is the only place it is
    // named - the stylesheets used to nail one to .screen, .chat and .spectate
    // and no longer say anything about fonts at all. Every language must fill
    // this in; a table that does not leaves its screens in Unity's default
    // font, which is why a test refuses to let one ship that way.
    [Tooltip("The face every screen is set in while this language is spoken. " +
             "Required: nothing else in the project names a font.")]
    [SerializeField] private FontAsset font;

    // Which machine opens in this language on its first run, before anybody
    // has been to the settings screen.
    //
    // Here rather than derived from the locale code, because "ru" is not a
    // rule that produces SystemLanguage.Russian - it is a fact about Russian,
    // and facts about a language belong to the language. A new table declares
    // its own and no code is touched to add one.
    [Tooltip("The operating system language this table answers for on a " +
             "machine that has never chosen one.")]
    [SerializeField] private SystemLanguage systemLanguage = SystemLanguage.Unknown;

    [SerializeField] private Entry[] entries = Array.Empty<Entry>();

    private Dictionary<string, string> lookup;

    public string Locale => locale;
    public string DisplayName => displayName;
    public FontAsset Font => font;
    public SystemLanguage SystemLanguage => systemLanguage;
    public int Count => entries != null ? entries.Length : 0;

    public IReadOnlyList<Entry> Entries => entries ?? Array.Empty<Entry>();

    // Built once and kept, because a screen asks this per label and a lobby
    // full of people asks it again every time the room changes.
    public bool TryTranslate(string english, out string translation)
    {
        EnsureLookup();

        if (!string.IsNullOrEmpty(english) &&
            lookup.TryGetValue(english, out translation) &&
            !string.IsNullOrEmpty(translation))
        {
            return true;
        }

        translation = english;
        return false;
    }

    public bool Knows(string english)
    {
        EnsureLookup();
        return !string.IsNullOrEmpty(english) && lookup.ContainsKey(english);
    }

    // The editor rewrites entries in place, so anything holding a lookup built
    // from the old array would answer with the old words.
    public void Invalidate()
    {
        lookup = null;
    }

    private void EnsureLookup()
    {
        if (lookup != null)
            return;

        lookup = new Dictionary<string, string>(Count, StringComparer.Ordinal);

        if (entries == null)
            return;

        for (int i = 0; i < entries.Length; i++)
        {
            string english = entries[i].English;

            if (string.IsNullOrEmpty(english))
                continue;

            // First one wins, and the duplicate is reported rather than
            // silently overwriting: two rows for one sentence means somebody
            // translated it twice and only one of them is being read.
            if (!lookup.TryAdd(english, entries[i].Translation))
            {
                Debug.LogWarning(
                    $"{nameof(LocaleTable)} '{name}' has more than one row for " +
                    $"\"{Shorten(english)}\". Only the first is used.",
                    this);
            }
        }
    }

    private static string Shorten(string value)
    {
        return value.Length <= 40 ? value : value.Substring(0, 37) + "...";
    }

#if UNITY_EDITOR
    // Editor-only and public rather than internal: the harvester lives in the
    // editor assembly, and widening this whole assembly's internals so one
    // tool can write one array would be the larger hole.
    public void SetEntriesFromEditor(Entry[] value)
    {
        entries = value ?? Array.Empty<Entry>();
        Invalidate();
    }

    private void OnValidate()
    {
        Invalidate();
    }
#endif
}
