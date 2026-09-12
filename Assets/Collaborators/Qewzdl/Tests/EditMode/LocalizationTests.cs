using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// The English is the key, which buys a great deal and costs one thing: nothing
// in the compiler notices when a screen starts saying something the table has
// never heard of. That is what this is for.
//
// It is also the only test that can notice, because the sentence and the table
// only meet at runtime, on a screen, in front of somebody.
public sealed class LocalizationTests
{
    private const string EnglishTablePath =
        "Assets/Collaborators/Qewzdl/Configs/Localization/Locale_English.asset";

    private static LocaleTable LoadEnglish()
    {
        LocaleTable table = AssetDatabase.LoadAssetAtPath<LocaleTable>(EnglishTablePath);
        Assert.That(table, Is.Not.Null, $"No locale table at '{EnglishTablePath}'.");
        return table;
    }

    // The harvest is a tool somebody has to remember to run. This is what
    // remembers for them.
    [Test]
    public void EverySentenceOnEveryScreenIsInTheTable()
    {
        LocaleTable table = LoadEnglish();
        List<string> onScreen = LocalizationHarvest.ReadScreens(out int screens);

        Assert.That(screens, Is.GreaterThan(0), "No screens were read.");

        List<string> missing = new();

        foreach (string english in onScreen)
        {
            if (!table.Knows(english))
                missing.Add(english);
        }

        Assert.That(
            missing,
            Is.Empty,
            "The screens say things the English table has never heard. Run " +
            "Tools > Wherever I Am > Localization > Harvest screens into table.");
    }

    // Translating to itself is what the English table is: every row is its own
    // answer. A row that says something else is a typo somebody made while
    // copying a table for another language into this one.
    [Test]
    public void TheEnglishTableTranslatesEnglishToItself()
    {
        LocaleTable table = LoadEnglish();

        Assert.That(table.Locale, Is.EqualTo("en"));
        Assert.That(table.Count, Is.GreaterThan(0));

        foreach (LocaleTable.Entry entry in table.Entries)
        {
            Assert.That(
                entry.Translation,
                Is.EqualTo(entry.English),
                $"The English table renders \"{entry.English}\" as " +
                $"\"{entry.Translation}\".");
        }
    }

    // Two rows for one sentence means one of them is never read, and which one
    // is an accident of ordering.
    [Test]
    public void NoSentenceIsInTheTableTwice()
    {
        LocaleTable table = LoadEnglish();
        HashSet<string> seen = new();
        List<string> twice = new();

        foreach (LocaleTable.Entry entry in table.Entries)
        {
            if (!string.IsNullOrEmpty(entry.English) && !seen.Add(entry.English))
                twice.Add(entry.English);
        }

        Assert.That(twice, Is.Empty, "Duplicated rows in the English table.");
    }

    // A language that ships half a table is worse than one that does not ship:
    // half a screen changes and the rest does not, which reads as broken
    // rather than as untranslated.
    [Test]
    public void EveryOtherLanguageCoversEverythingTheEnglishSays()
    {
        LocaleTable english = LoadEnglish();
        string[] guids = AssetDatabase.FindAssets(
            "t:LocaleTable",
            new[] { "Assets/Collaborators/Qewzdl/Configs/Localization" });

        Assert.That(guids.Length, Is.GreaterThan(1), "Only one language ships.");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            LocaleTable table = AssetDatabase.LoadAssetAtPath<LocaleTable>(path);

            if (table == null || table == english)
                continue;

            List<string> missing = new();
            List<string> untranslated = new();

            foreach (LocaleTable.Entry entry in english.Entries)
            {
                if (string.IsNullOrEmpty(entry.English))
                    continue;

                if (!table.TryTranslate(entry.English, out string translation))
                {
                    missing.Add(entry.English);
                    continue;
                }

                // A name stays a name in every language, so a row that answers
                // with its own English is only suspect when it is a sentence.
                if (translation == entry.English && entry.English.Contains(" "))
                    untranslated.Add(entry.English);
            }

            Assert.That(
                missing,
                Is.Empty,
                $"'{table.name}' has no row for these.");

            Assert.That(
                untranslated.Count,
                Is.LessThan(2),
                $"'{table.name}' leaves these in English: " +
                string.Join(" | ", untranslated));
        }
    }

    // Anything the table does not know comes back as it went in, which is what
    // makes a missing row a stale sentence rather than a blank screen.
    [Test]
    public void AnUnknownSentenceComesBackUnchanged()
    {
        LocaleTable table = LoadEnglish();

        Assert.That(
            table.TryTranslate("Nothing on any screen says this.", out string result),
            Is.False);

        Assert.That(result, Is.EqualTo("Nothing on any screen says this."));
        Assert.That(table.TryTranslate(null, out string none), Is.False);
        Assert.That(none, Is.Null);
    }

    // The bug this is here for: a document that binds from OnEnable hands its
    // tree over during the Awake phase, and the services are composed in a
    // Start. That tree was told there was no language yet and never asked
    // again - a document binds once - so the settings screen was the only
    // screen in the game that never changed language, however often the player
    // changed it.
    [Test]
    public void ATreeOfferedBeforeTheServiceArrivesIsStillTranslated()
    {
        FakeLocalization fake = new();
        Label label = new("Start");
        VisualElement root = new();
        root.Add(label);

        try
        {
            // Nothing composed yet, so nothing to translate with.
            UiLocalization.Apply(root);
            Assert.That(label.text, Is.EqualTo("Start"));

            UiLocalization.Use(fake);

            Assert.That(
                label.text,
                Is.EqualTo("<Start>"),
                "A tree handed over before composition never got translated.");
        }
        finally
        {
            UiLocalization.Release(fake);
        }
    }

    // Every sentence comes back wrapped, so a translated label is obvious and
    // an untranslated one cannot be mistaken for a lucky match.
    private sealed class FakeLocalization : ILocalizationService
    {
        public string Locale => "test";

        public IReadOnlyList<LocaleOption> AvailableLocales { get; } =
            new[] { new LocaleOption("test", "Test") };

        public event Action LocaleChanged;

        public bool TrySetLocale(string locale)
        {
            LocaleChanged?.Invoke();
            return true;
        }

        public string Translate(string english)
        {
            return string.IsNullOrEmpty(english) ? english : $"<{english}>";
        }
    }

    // The service is what everything else talks to, and it has to be safe
    // before anything has been composed - a screen that binds early must get
    // its own English back rather than an empty label.
    [Test]
    public void TheServiceFallsBackToEnglishBeforeItIsStarted()
    {
        GameObject host = new("Localization service test");

        try
        {
            LocalizationService service = host.AddComponent<LocalizationService>();

            Assert.That(service.Translate("Start"), Is.EqualTo("Start"));
            Assert.That(service.Translate(string.Empty), Is.Empty);
            Assert.That(service.Translate(null), Is.Null);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }
}
