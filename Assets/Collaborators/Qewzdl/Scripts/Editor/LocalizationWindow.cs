using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// One page for the whole of the localisation.
//
// There used to be a single menu item that harvested the screens into whatever
// asset happened to be selected, which meant the one thing you could do to a
// table was the one thing you rarely wanted. Everything else - seeing what a
// language was missing, what it had left in English, whether it had a font it
// could be read in - meant opening the assets and scrolling.
//
// The English table is the catalogue, so it is the list. Every other language
// is read against it: a row it has no answer for is missing, a row that
// answers with the English is untranslated, and both are visible from here
// without counting anything by hand.
public sealed class LocalizationWindow : EditorWindow
{
    private const string Folder = "Assets/Collaborators/Qewzdl/Configs/Localization";

    private const string EnglishPath = Folder + "/Locale_English.asset";

    private readonly List<LocaleTable> tables = new();
    private readonly List<LocaleTable.Entry> rows = new();

    private LocaleTable english;
    private LocaleTable chosen;
    private Vector2 scroll;
    private string search = string.Empty;
    private bool onlyUnfinished;

    [MenuItem("Tools/Wherever I Am/Localization", false, 110)]
    private static void OpenWindow()
    {
        LocalizationWindow window = GetWindow<LocalizationWindow>("Localization");
        window.minSize = new Vector2(620f, 420f);
        window.Show();
    }

    private void OnEnable()
    {
        Reload();
    }

    private void OnFocus()
    {
        Reload();
        Repaint();
    }

    // Read off disk rather than kept, because the assets are edited by hand as
    // well as from here and this window is not the owner of anything.
    private void Reload()
    {
        tables.Clear();
        english = AssetDatabase.LoadAssetAtPath<LocaleTable>(EnglishPath);

        foreach (string guid in AssetDatabase.FindAssets("t:LocaleTable", new[] { Folder }))
        {
            LocaleTable table =
                AssetDatabase.LoadAssetAtPath<LocaleTable>(AssetDatabase.GUIDToAssetPath(guid));

            if (table != null)
                tables.Add(table);
        }

        if (chosen == null || !tables.Contains(chosen))
            chosen = tables.Count > 0 ? tables[0] : null;

        TakeRows();
    }

    private void TakeRows()
    {
        rows.Clear();

        if (chosen == null)
            return;

        foreach (LocaleTable.Entry entry in chosen.Entries)
            rows.Add(entry);
    }

    private void OnGUI()
    {
        if (english == null)
        {
            EditorGUILayout.HelpBox(
                $"No English table at '{EnglishPath}'. It is the catalogue " +
                "every other language is read against.",
                MessageType.Error);

            return;
        }

        DrawToolbar();
        DrawSummary();
        DrawRows();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            int index = Mathf.Max(0, tables.IndexOf(chosen));
            string[] names = new string[tables.Count];

            for (int i = 0; i < tables.Count; i++)
                names[i] = $"{tables[i].DisplayName}  ({tables[i].Locale})";

            int picked = EditorGUILayout.Popup(index, names, EditorStyles.toolbarPopup,
                GUILayout.Width(200f));

            if (picked != index && picked >= 0 && picked < tables.Count)
            {
                chosen = tables[picked];
                TakeRows();
                GUI.FocusControl(null);
            }

            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);

            onlyUnfinished = GUILayout.Toggle(
                onlyUnfinished,
                "Unfinished only",
                EditorStyles.toolbarButton,
                GUILayout.Width(110f));

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Read the screens", EditorStyles.toolbarButton,
                    GUILayout.Width(130f)))
            {
                ReadTheScreens();
            }

            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(50f)))
                AssetDatabase.SaveAssets();
        }
    }

    // Into the English table only. The catalogue is what the screens describe;
    // handing every other language a row that answers with English would fill
    // them with placeholders that read as finished work.
    private void ReadTheScreens()
    {
        List<string> found = LocalizationHarvest.ReadScreens(out int files);
        LocalizationHarvest.Merge(english, found, out int added, out int kept, out int unused);

        EditorUtility.SetDirty(english);
        AssetDatabase.SaveAssets();
        TakeRows();

        ShowNotification(new GUIContent(
            $"{found.Count} sentences on {files} screens: {added} new, " +
            $"{kept} already there, {unused} off screen."));
    }

    private void DrawSummary()
    {
        if (chosen == null)
            return;

        int missing = 0;
        int untranslated = 0;

        foreach (LocaleTable.Entry entry in english.Entries)
        {
            if (string.IsNullOrEmpty(entry.English))
                continue;

            if (!chosen.TryTranslate(entry.English, out string translation))
            {
                missing++;
                continue;
            }

            // A name is the same in every language, so only a sentence that
            // answers with its own English is unfinished work.
            if (translation == entry.English && entry.English.Contains(" "))
                untranslated++;
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(
                $"{chosen.Count} rows · {missing} missing · {untranslated} still in English",
                EditorStyles.boldLabel);

            EditorGUILayout.LabelField(
                chosen.Font != null
                    ? $"Font: {chosen.Font.name}"
                    : "Font: none - every screen would be set in Unity's default",
                chosen.Font != null
                    ? EditorStyles.miniLabel
                    : ErrorStyle());

            EditorGUILayout.LabelField(
                $"Plurals: {chosen.PluralRule} · opens on a {chosen.SystemLanguage} machine",
                EditorStyles.miniLabel);
        }
    }

    private static GUIStyle ErrorStyle()
    {
        GUIStyle style = new(EditorStyles.miniLabel);
        style.normal.textColor = new Color(0.9f, 0.4f, 0.35f);
        return style;
    }

    private void DrawRows()
    {
        if (chosen == null)
            return;

        bool isCatalogue = chosen == english;
        scroll = EditorGUILayout.BeginScrollView(scroll);

        for (int i = 0; i < english.Count; i++)
        {
            LocaleTable.Entry key = english.Entries[i];

            if (string.IsNullOrEmpty(key.English) || !Matches(key.English))
                continue;

            int row = IndexOf(key.English);
            LocaleTable.Entry entry = row >= 0
                ? rows[row]
                : new LocaleTable.Entry { English = key.English };

            bool unfinished =
                row < 0 ||
                string.IsNullOrEmpty(entry.Translation) ||
                (entry.Translation == key.English && key.English.Contains(" "));

            if (onlyUnfinished && !isCatalogue && !unfinished)
                continue;

            DrawRow(key, ref entry, row, unfinished, isCatalogue);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawRow(
        LocaleTable.Entry key,
        ref LocaleTable.Entry entry,
        int row,
        bool unfinished,
        bool isCatalogue)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.SelectableLabel(
                key.English,
                unfinished && !isCatalogue ? ErrorStyle() : EditorStyles.wordWrappedMiniLabel,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));

            using (EditorGUI.ChangeCheckScope changed = new())
            {
                // The English table answers every row with itself - that is
                // what makes it the catalogue - so its translation column is
                // not somewhere to type. Its plural forms are.
                using (new EditorGUI.DisabledScope(isCatalogue))
                    entry.Translation = EditorGUILayout.TextArea(entry.Translation ?? string.Empty);

                if (chosen.PluralRule != PluralRule.None && key.English.Contains("{0}"))
                    DrawForms(ref entry);

                if (changed.changed)
                    Write(row, entry);
            }
        }
    }

    private void DrawForms(ref LocaleTable.Entry entry)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            entry.One = Form("one", entry.One);

            if (chosen.PluralRule == PluralRule.EastSlavic)
            {
                entry.Few = Form("few", entry.Few);
                entry.Many = Form("many", entry.Many);
            }
        }
    }

    private static string Form(string label, string value)
    {
        using (new EditorGUILayout.VerticalScope())
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            return EditorGUILayout.TextField(value ?? string.Empty);
        }
    }

    private void Write(int row, LocaleTable.Entry entry)
    {
        if (row >= 0)
            rows[row] = entry;
        else
            rows.Add(entry);

        chosen.SetEntriesFromEditor(rows.ToArray());
        EditorUtility.SetDirty(chosen);
    }

    private int IndexOf(string english)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].English == english)
                return i;
        }

        return -1;
    }

    private bool Matches(string english)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        if (english.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        int row = IndexOf(english);

        return row >= 0 &&
               !string.IsNullOrEmpty(rows[row].Translation) &&
               rows[row].Translation.IndexOf(
                   search, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
