using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Reads every screen and writes down every word on it, for the localisation
// window to call. It used to be a menu item of its own, which meant the one
// thing you could do to a table was the one thing you rarely wanted.
//
// The English table is a catalogue before it is a translation: it is how
// anybody sees all the game's copy at once instead of opening eight documents,
// and it is what a second language is copied from. Keeping it by hand would
// mean it was wrong within a week, so it is not kept by hand.
//
// Only the markup is harvested. The sentences that live in components are set
// from code and reach the table through the call that shows them, which the
// missing-row report below is there to catch.
public static class LocalizationHarvest
{
    private const string ScreensFolder = "Assets/Collaborators/Qewzdl/UI/Screens";

    private static readonly Regex TextAttribute = new(
        @"<ui:(?<tag>Label|Button)\b[^>]*?\btext=""(?<text>[^""]*)""",
        RegexOptions.Compiled);

    // Every sentence the markup writes, in the order it is read on screen, so
    // a translator works down the table the way a player works down the page.
    public static List<string> ReadScreens(out int fileCount)
    {
        List<string> found = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        string[] files = Directory.GetFiles(ScreensFolder, "*.uxml");
        fileCount = files.Length;
        Array.Sort(files, StringComparer.Ordinal);

        foreach (string file in files)
        {
            foreach (Match match in TextAttribute.Matches(File.ReadAllText(file)))
            {
                string text = match.Groups["text"].Value;

                if (string.IsNullOrWhiteSpace(text) || !seen.Add(text))
                    continue;

                found.Add(text);
            }
        }

        return found;
    }

    // Rows already in the table keep their translation and their place. A row
    // nobody says any more is left where it is rather than deleted: with the
    // English as the key, a sentence that was edited looks exactly like a
    // sentence that was removed, and throwing away somebody's translation on
    // that guess is not a thing a tool should do quietly.
    public static void Merge(
        LocaleTable table,
        List<string> found,
        out int added,
        out int kept,
        out int unused)
    {
        Dictionary<string, LocaleTable.Entry> existing = new(StringComparer.Ordinal);
        List<LocaleTable.Entry> rows = new();

        foreach (LocaleTable.Entry entry in table.Entries)
        {
            if (!string.IsNullOrEmpty(entry.English))
                existing[entry.English] = entry;
        }

        added = 0;
        kept = 0;

        foreach (string english in found)
        {
            bool known = existing.TryGetValue(english, out LocaleTable.Entry entry);

            if (known)
                kept++;
            else
                added++;

            // The whole row is carried over, not a copy of two of its columns.
            // Rebuilding it lost the plural forms - silently, and only for
            // rows that were on a screen, because the ones that were not got
            // carried over whole further down.
            if (!known)
            {
                entry = new LocaleTable.Entry
                {
                    English = english,

                    // A new row translates to itself. In the English table
                    // that is the answer; in any other it is a placeholder
                    // that reads as untranslated rather than as blank.
                    Translation = english
                };
            }
            else if (string.IsNullOrEmpty(entry.Translation))
            {
                entry.Translation = english;
            }

            rows.Add(entry);
        }

        unused = 0;

        foreach (LocaleTable.Entry entry in table.Entries)
        {
            if (string.IsNullOrEmpty(entry.English) || found.Contains(entry.English))
                continue;

            unused++;
            rows.Add(entry);
        }

        table.SetEntriesFromEditor(rows.ToArray());
    }
}
