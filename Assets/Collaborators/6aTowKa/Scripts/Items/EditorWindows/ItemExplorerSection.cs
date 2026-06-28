using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Explorer section of the Item Creator window: a "проводник" over the items the creator
// generated. Lists every item prefab under prefabDir, and for the selected one lets you
// jump to its script / prefab / data asset (select + ping in Project) and edit its config
// inline via an embedded inspector. Reads pre-existing item/core classes through
// SerializedObject only — never modifies them.
public partial class ItemCreatorWindow
{
    private sealed class ItemEntry
    {
        public GameObject Prefab;
        public string PrefabPath;
        public string Name;
        public string BaseClass;
        public UnityEngine.Object Data;
        public MonoScript Script;
    }

    private bool explorerFoldout;
    private bool explorerBuilt;
    private string explorerSearch = "";
    private string selectedPrefabPath = "";
    private readonly List<ItemEntry> explorerEntries = new List<ItemEntry>();
    private Editor dataEditor;

    // Shared palette (used here and by the archetype buttons in the main file).
    // Neutral grey — selection brightens the default button (>1 multiplier) so it
    // reads as highlighted without going blue or blending into the editor background.
    internal static readonly Color SelectionColor = new Color(0.82f, 0.82f, 0.82f);
    internal static readonly Color DangerColor    = new Color(0.84f, 0.36f, 0.36f);
    internal static readonly Color SeparatorColor = new Color(1f, 1f, 1f, 0.35f);

    // ── Draw ──────────────────────────────────────────────────────────────────
    private void DrawExplorerSection()
    {
        DrawSeparator();

        explorerFoldout = EditorGUILayout.Foldout(explorerFoldout, "Explorer", true, EditorStyles.foldoutHeader);
        if (!explorerFoldout) return;

        if (!explorerBuilt) BuildExplorerList();

        EditorGUI.indentLevel++;

        EditorGUILayout.BeginHorizontal();
        explorerSearch = EditorGUILayout.TextField(explorerSearch, EditorStyles.toolbarSearchField);
        if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(70)))
            BuildExplorerList();
        EditorGUILayout.EndHorizontal();

        if (explorerEntries.Count == 0)
        {
            EditorGUILayout.HelpBox($"No items found in {prefabDir}.\nCreate one above, then press Refresh.", MessageType.Info);
            EditorGUI.indentLevel--;
            return;
        }

        DrawItemList();

        var selected = explorerEntries.Find(e => e.PrefabPath == selectedPrefabPath);
        if (selected != null)
            DrawSelectedItemPanel(selected);

        EditorGUI.indentLevel--;
    }

    private void DrawItemList()
    {
        var prevBg = GUI.backgroundColor;
        foreach (var entry in explorerEntries)
        {
            if (!string.IsNullOrEmpty(explorerSearch) &&
                entry.Name.IndexOf(explorerSearch, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            bool isSelected = entry.PrefabPath == selectedPrefabPath;
            GUI.backgroundColor = isSelected ? SelectionColor : prevBg;

            if (GUILayout.Button($"{entry.Name}    <{entry.BaseClass}>", EditorStyles.miniButton))
            {
                if (isSelected)
                {
                    // Click the selected row again to deselect.
                    selectedPrefabPath = "";
                    RebuildDataEditor(null);
                }
                else
                {
                    selectedPrefabPath = entry.PrefabPath;
                    RebuildDataEditor(entry.Data);
                }
                GUI.FocusControl(null);
            }
        }
        GUI.backgroundColor = prevBg;
    }

    private void DrawSelectedItemPanel(ItemEntry entry)
    {
        EditorGUILayout.Space(4);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(entry.Name, EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(entry.Script == null))
                if (GUILayout.Button("Script")) PingInProject(entry.Script);
            if (GUILayout.Button("Prefab")) PingInProject(entry.Prefab);
            using (new EditorGUI.DisabledScope(entry.Data == null))
                if (GUILayout.Button("Data")) PingInProject(entry.Data);
            if (GUILayout.Button("Preview"))
                ViewmodelPreviewSceneCreator.OpenOrCreatePreviewScene(entry.Prefab);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            // Destructive — tinted red and confirmed.
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = DangerColor;
            bool delete = GUILayout.Button("Delete item");
            GUI.backgroundColor = prevBg;
            if (delete)
            {
                DeleteItem(entry);
                return;
            }

            EditorGUILayout.Space(6);

            if (entry.Data == null)
            {
                EditorGUILayout.HelpBox("This item has no data asset assigned.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Config", EditorStyles.boldLabel);
            if (dataEditor == null || dataEditor.target != entry.Data)
                RebuildDataEditor(entry.Data);

            // Embedded inspectors misbehave under a non-zero indent — reset around it.
            // wideMode keeps Vector3/Vector4 fields on a single line in this narrow window.
            int prevIndent = EditorGUI.indentLevel;
            bool prevWide = EditorGUIUtility.wideMode;
            EditorGUI.indentLevel = 0;
            EditorGUIUtility.wideMode = true;
            dataEditor?.OnInspectorGUI();
            EditorGUIUtility.wideMode = prevWide;
            EditorGUI.indentLevel = prevIndent;
        }
    }

    // ── Data ──────────────────────────────────────────────────────────────────
    private void BuildExplorerList()
    {
        explorerEntries.Clear();
        explorerBuilt = true;

        if (string.IsNullOrEmpty(prefabDir) || !AssetDatabase.IsValidFolder(prefabDir))
            return;

        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { prefabDir }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;

            var comp = go.GetComponentInChildren<InteractableObject>(true);
            if (comp == null) continue;

            var so = new SerializedObject(comp);
            var type = comp.GetType();

            explorerEntries.Add(new ItemEntry
            {
                Prefab     = go,
                PrefabPath = path,
                Name       = go.name,
                BaseClass  = type.BaseType != null ? type.BaseType.Name : type.Name,
                Data       = so.FindProperty("data")?.objectReferenceValue,
                Script     = MonoScript.FromMonoBehaviour(comp),
            });
        }

        explorerEntries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    private void DeleteItem(ItemEntry entry)
    {
        if (!EditorUtility.DisplayDialog(
                "Delete item",
                $"Delete '{entry.Name}'?\n\nThis removes the prefab, its data asset and its script. This cannot be undone.",
                "Delete", "Cancel"))
            return;

        // Drop the embedded editor before its target is deleted.
        RebuildDataEditor(null);

        var paths = new List<string>();
        if (!string.IsNullOrEmpty(entry.PrefabPath)) paths.Add(entry.PrefabPath);
        if (entry.Data != null)   paths.Add(AssetDatabase.GetAssetPath(entry.Data));
        if (entry.Script != null) paths.Add(AssetDatabase.GetAssetPath(entry.Script));

        foreach (var p in paths)
            if (!string.IsNullOrEmpty(p)) AssetDatabase.DeleteAsset(p);

        AssetDatabase.Refresh();

        selectedPrefabPath = "";
        BuildExplorerList();
        Debug.Log($"[ItemExplorer] Deleted '{entry.Name}'.");
    }

    private static void DrawSeparator()
    {
        EditorGUILayout.Space(10);
        var rect = EditorGUILayout.GetControlRect(false, 2);
        EditorGUI.DrawRect(rect, SeparatorColor);
        EditorGUILayout.Space(10);
    }

    private void RebuildDataEditor(UnityEngine.Object data)
    {
        if (dataEditor != null)
        {
            UnityEngine.Object.DestroyImmediate(dataEditor);
            dataEditor = null;
        }
        if (data != null)
            dataEditor = Editor.CreateEditor(data);
    }

    // Invalidate the cached list so it rebuilds on the next draw. Hooked to
    // EditorApplication.projectChanged so the list auto-updates when items are
    // added/removed/moved; the Refresh button stays as a manual fallback.
    private void MarkExplorerDirty()
    {
        explorerBuilt = false;
        Repaint();
    }

    private void DisposeExplorer()
    {
        if (dataEditor != null)
        {
            UnityEngine.Object.DestroyImmediate(dataEditor);
            dataEditor = null;
        }
    }

    private static void PingInProject(UnityEngine.Object obj)
    {
        if (obj == null) return;
        Selection.activeObject = obj;
        EditorGUIUtility.PingObject(obj);
    }
}
