using System;
using System.Collections.Generic;
using UnityEngine;

// The one place that knows which language is being spoken.
//
// Composed by ProjectContext and registered in the global scope beside the
// settings service, because language outlives every scene and everything that
// draws a word needs it.
[DisallowMultipleComponent]
public sealed class LocalizationService : MonoBehaviour, ILocalizationService
{
    [Tooltip("Every language the build ships with. The first is the one the " +
             "game falls back to, and it is the English catalogue.")]
    [SerializeField] private LocaleTable[] tables = Array.Empty<LocaleTable>();

    private readonly List<LocaleOption> options = new();
    private LocaleTable active;
    private ISettingsService settingsService;

    public string Locale => active != null ? active.Locale : string.Empty;

    public IReadOnlyList<LocaleOption> AvailableLocales => options;

    public event Action LocaleChanged;

    public bool Initialize()
    {
        if (tables == null || tables.Length == 0 || tables[0] == null)
        {
            Debug.LogError(
                $"{nameof(LocalizationService)} has no tables. Every word in " +
                "the game would come back untranslated.",
                this);

            return false;
        }

        options.Clear();

        for (int i = 0; i < tables.Length; i++)
        {
            LocaleTable table = tables[i];

            if (table == null)
                continue;

            options.Add(new LocaleOption(table.Locale, table.DisplayName));
        }

        active = tables[0];
        return true;
    }

    // The stored language, and every change to it afterwards.
    //
    // Constructed after the settings rather than before, because the settings
    // are read off disk and this has to be up before any of that: a screen
    // built while the language was still unknown would be built in English and
    // stay that way. So it starts in the fallback and moves to the player's
    // choice as soon as there is one, which is one frame later and before
    // anything has been drawn.
    public void Construct(ISettingsService settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        if (ReferenceEquals(settingsService, settings))
            return;

        ReleaseSettingsService();
        settingsService = settings;
        settingsService.SettingsChanged += ApplySettings;
        ApplySettings();
    }

    public void ReleaseSettingsService()
    {
        if (settingsService == null)
            return;

        settingsService.SettingsChanged -= ApplySettings;
        settingsService = null;
    }

    private void ApplySettings()
    {
        if (settingsService != null)
            TrySetLocale(settingsService.Current.locale);
    }

    private void OnDestroy()
    {
        ReleaseSettingsService();
    }

    public string Translate(string english)
    {
        if (string.IsNullOrEmpty(english))
            return english;

        if (active == null)
            return english;

        active.TryTranslate(english, out string translation);
        return translation;
    }

    public bool TrySetLocale(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale) || tables == null)
            return false;

        for (int i = 0; i < tables.Length; i++)
        {
            LocaleTable table = tables[i];

            if (table == null ||
                !string.Equals(table.Locale, locale, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ReferenceEquals(table, active))
                return true;

            active = table;
            LocaleChanged?.Invoke();
            return true;
        }

        return false;
    }

#if UNITY_EDITOR
    internal LocaleTable[] TablesForEditor => tables;
#endif
}
