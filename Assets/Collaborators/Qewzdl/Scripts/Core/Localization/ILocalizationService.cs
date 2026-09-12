using System;
using System.Collections.Generic;

/// <summary>
/// Turns the English a screen was written in into the language being played.
/// </summary>
/// <remarks>
/// The English is the key. There is no second name to keep in step with the
/// first, which is the same reason an objective dropped its hand-typed id, and
/// it means the fallback for anything untranslated is the sentence the author
/// actually wrote rather than a dotted path leaking onto the screen.
///
/// It also makes staleness honest. This project edits its own copy constantly,
/// and under invented keys a rewritten sentence silently keeps whatever
/// translation was attached to the old one. Here a rewritten sentence is a new
/// key: the old row goes unused and the harvest reports it, which is true -
/// the sentence changed, so the translation is out of date.
/// </remarks>
public interface ILocalizationService
{
    /// <summary>The locale currently being spoken, as a BCP 47 tag.</summary>
    string Locale { get; }

    /// <summary>
    /// Every language the build ships with, in the order they are offered.
    /// Named in themselves rather than in English: somebody looking for their
    /// own language is looking for the word they call it by.
    /// </summary>
    IReadOnlyList<LocaleOption> AvailableLocales { get; }

    /// <summary>
    /// Speak a different language. False when the build does not have it,
    /// which leaves the current one alone rather than falling silent.
    /// </summary>
    bool TrySetLocale(string locale);

    /// <summary>
    /// The given English, translated. Never null and never empty for non-empty
    /// input: anything the table does not know comes back as it went in.
    /// </summary>
    string Translate(string english);

    /// <summary>
    /// Raised when the language changes, so anything already on screen can ask
    /// again. Screens are built from this service rather than caching it, so
    /// most of them only need to rebuild.
    /// </summary>
    event Action LocaleChanged;
}

/// <summary>A language, as a picker sees it.</summary>
public readonly struct LocaleOption
{
    public readonly string Locale;
    public readonly string DisplayName;

    public LocaleOption(string locale, string displayName)
    {
        Locale = locale;
        DisplayName = displayName;
    }
}
