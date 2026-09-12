using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;

// The markup has a harvester and a test that says every sentence on a screen
// is in the table. The code had neither, and that is where the drift came
// from: a hundred sentences sat in serialized fields and literals for as long
// as this project has had screens, and they were found by reading every file
// by hand rather than by anything noticing.
//
// This is what notices. It is deliberately narrow - what a field is worth in
// English, and what a line writes straight onto an element - because a wider
// net catches log messages and element names, and a guard that cries wolf gets
// switched off.
public sealed class LocalizationSourceTests
{
    private const string TablePath =
        "Assets/Collaborators/Qewzdl/Configs/Localization/Locale_English.asset";

    // Where the game talks to a player. Editor tools, debug windows and the
    // engine-facing half of the project are not in here on purpose.
    private static readonly string[] Folders =
    {
        "Assets/Collaborators/Qewzdl/Scripts/UI",
        "Assets/Collaborators/Qewzdl/Scripts/Lobby",
        "Assets/Collaborators/Qewzdl/Scripts/Chat",
        "Assets/Collaborators/Qewzdl/Scripts/Game/Players",
        "Assets/Collaborators/Qewzdl/Scripts/GameFlow",
        "Assets/Collaborators/Qewzdl/Scripts/Network"
    };

    // Fields whose English is not for reading, each for a reason that is about
    // the field rather than about anybody's patience.
    private static readonly HashSet<string> NotForReading = new()
    {
        // Composed on the server with a player's name already inside them and
        // broadcast finished, so the client receives a sentence that is in no
        // table and never can be. Translating these means sending the key and
        // the name apart and formatting on the client - a change to the
        // protocol rather than to the localisation.
        "playerJoinedMessageFormat",
        "playerLeftMessageFormat",
        "playerKickedMessageFormat",
        "playerLostConnectionMessageFormat",
        "playerCameBackMessageFormat",

        // Diagnostics. These go into the transition reason the game flow keeps
        // for the log, and no screen ever shows one.
        "hitReason",
        "lastPlayerCaughtReason",
        "startReason",
        "completionReason",
        "failureReason",

        // Not a sentence at all: a Unity tag, matched against a collider.
        "requiredTag"
    };

    // [SerializeField] sits on the same line as the declaration everywhere in
    // this project; the value may run onto the next. A literal containing a
    // semicolon would cut the match short - the test would then report a
    // half-sentence as missing, which is loud rather than silent.
    private static readonly Regex SerializedString = new(
        @"\[SerializeField\]\s*private\s+string\s+(?<name>\w+)\s*=\s*(?<value>[^;]*);",
        RegexOptions.Singleline);

    // What a line hands straight to a player: written onto an element, or
    // raised as an error. Anything else a string is doing here is its own
    // business.
    private static readonly Regex Shown = new(@"\.text\s*=|ShowError\(");

    private static readonly Regex Literal = new(@"""((?:[^""\\]|\\.)*)""");

    [Test]
    public void EverySentenceTheCodeSaysIsInTheTable()
    {
        LocaleTable table = AssetDatabase.LoadAssetAtPath<LocaleTable>(TablePath);
        Assert.That(table, Is.Not.Null, $"No locale table at '{TablePath}'.");

        List<string> missing = new();
        int files = 0;

        foreach (string folder in Folders)
        {
            Assert.That(
                Directory.Exists(folder),
                $"'{folder}' is gone, so this test has stopped reading it.");

            foreach (string path in Directory.GetFiles(
                         folder, "*.cs", SearchOption.AllDirectories))
            {
                files++;
                Read(File.ReadAllText(path), table, path, missing);
            }
        }

        Assert.That(files, Is.GreaterThan(0), "No source files were read.");

        Assert.That(
            missing,
            Is.Empty,
            "The code says things the table has never heard. Add a row for " +
            "each, or - if it is never read by a player - say why in " +
            $"{nameof(NotForReading)}.\n  " + string.Join("\n  ", missing));
    }

    private static void Read(
        string source,
        LocaleTable table,
        string path,
        List<string> missing)
    {
        string file = Path.GetFileName(path);

        foreach (Match declaration in SerializedString.Matches(source))
        {
            string name = declaration.Groups["name"].Value;

            if (NotForReading.Contains(name))
                continue;

            string value = Join(declaration.Groups["value"].Value);

            if (IsWorthTranslating(value) && !table.Knows(value))
                missing.Add($"{file} · {name} · \"{value}\"");
        }

        foreach (string line in source.Split('\n'))
        {
            if (!Shown.IsMatch(line) || line.TrimStart().StartsWith("//"))
                continue;

            foreach (Match literal in Literal.Matches(line))
            {
                string value = Unescape(literal.Groups[1].Value);

                // A sentence rather than a name: one word with no space is an
                // element to look up or a class to add, and this line is full
                // of both.
                if (!value.Contains(" ") || !IsWorthTranslating(value))
                    continue;

                if (!table.Knows(value))
                    missing.Add($"{file} · \"{value}\"");
            }
        }
    }

    // Every literal in the declaration, joined the way the compiler joins
    // them, so a sentence split over two lines is looked up whole.
    private static string Join(string value)
    {
        System.Text.StringBuilder text = new();

        foreach (Match literal in Literal.Matches(value))
            text.Append(Unescape(literal.Groups[1].Value));

        return text.ToString();
    }

    private static string Unescape(string value)
    {
        return value.Replace("\\\"", "\"").Replace("\\n", "\n");
    }

    // Placeholders and punctuation are the same in every language, and a row
    // for "{0}/{1}" would only be a row nobody ever reads.
    private static bool IsWorthTranslating(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        int letters = 0;

        foreach (char character in value)
        {
            if (char.IsLetter(character))
                letters++;
        }

        return letters >= 2;
    }
}
