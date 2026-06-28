using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ItemCreatorPostCompile
{
    private const string PendingKey = "ItemCreator_Pending";

    // Runs after every domain reload (= after compilation). Check if a prefab is pending.
    static ItemCreatorPostCompile()
    {
        if (!EditorPrefs.HasKey(PendingKey)) return;

        var json = EditorPrefs.GetString(PendingKey);
        EditorPrefs.DeleteKey(PendingKey);

        PendingItemData pending;
        try { pending = JsonUtility.FromJson<PendingItemData>(json); }
        catch { return; }

        EditorApplication.delayCall += () => FinalizePrefab(pending);
    }

    private static void FinalizePrefab(PendingItemData pending)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(t => t.Name == pending.ClassName);

        if (type == null)
        {
            Debug.LogError($"[ItemCreator] Type '{pending.ClassName}' not found after compilation. Try reopening the Item Creator and creating again.");
            return;
        }

        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(pending.BasePrefabPath);
        if (basePrefab == null)
        {
            Debug.LogError($"[ItemCreator] Base prefab not found at '{pending.BasePrefabPath}'.");
            return;
        }

        var dataAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(pending.DataAssetPath);

        var instance = PrefabUtility.InstantiatePrefab(basePrefab) as GameObject;
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        var component = instance.AddComponent(type);
        if (dataAsset != null)
        {
            var so = new SerializedObject(component);
            var dataProp = so.FindProperty("data");
            if (dataProp != null)
            {
                dataProp.objectReferenceValue = dataAsset;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        var dir = Path.GetDirectoryName(pending.PrefabPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

        PrefabUtility.SaveAsPrefabAsset(instance, pending.PrefabPath);
        UnityEngine.Object.DestroyImmediate(instance);

        AssetDatabase.Refresh();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pending.PrefabPath);
        if (prefab != null) EditorGUIUtility.PingObject(prefab);

        Debug.Log($"[ItemCreator] Created: {pending.ClassName} prefab at {pending.PrefabPath}");
    }

    [Serializable]
    public class PendingItemData
    {
        public string ClassName;
        public string BasePrefabPath;
        public string DataAssetPath;
        public string PrefabPath;
    }
}

public partial class ItemCreatorWindow : EditorWindow
{
    // ── EditorPrefs keys ──────────────────────────────────────────────────────
    private const string PrefScriptDir   = "ItemCreator_ScriptDir";
    private const string PrefDataDir     = "ItemCreator_DataDir";
    private const string PrefPrefabDir   = "ItemCreator_PrefabDir";
    private const string PrefItemPrefab    = "ItemCreator_ItemPrefab";
    private const string PrefDragPrefab    = "ItemCreator_DragPrefab";
    private const string PrefInterPrefab   = "ItemCreator_InterPrefab";
    private const string PrefDefaultSprite = "ItemCreator_DefaultSprite";
    private const string PendingKey        = "ItemCreator_Pending";

    // ── Default paths ──────────────────────────────────────────────────────────
    private const string DefaultScriptDir   = "Assets/Collaborators/6aTowKa/Scripts/Items/Generated";
    private const string DefaultDataDir     = "Assets/Collaborators/6aTowKa/Configs/Items";
    private const string DefaultPrefabDir   = "Assets/Collaborators/6aTowKa/Prefabs/Items";

    // ── State ─────────────────────────────────────────────────────────────────
    private enum Archetype { Item, Draggable, Interactable }

    private string itemName = "";
    private Archetype archetype = Archetype.Item;

    // Item abilities
    private bool abilityUsable;
    private bool abilityActivatable;

    // Draggable / Interactable abilities
    private bool abilityRequiresItem;

    // Interaction icon
    private Sprite interactionSprite;

    // Settings
    private bool settingsFoldout = false;
    private string scriptDir;
    private string dataDir;
    private string prefabDir;
    private GameObject itemBasePrefab;
    private GameObject draggableBasePrefab;
    private GameObject interactableBasePrefab;
    private Sprite defaultInteractionSprite;

    // Auto section
    private bool autoFoldout = false;
    private int nextItemId = -1;

    // Scroll
    private Vector2 scroll;

    // Styles (lazy-init)
    private GUIStyle _archetypeButtonStyle;
    private GUIStyle _archetypeButtonActiveStyle;
    private GUIStyle _headerStyle;
    private bool _stylesInitialized;

    // ── Open ─────────────────────────────────────────────────────────────────
    [MenuItem("Tools/Wherever I Am/Item Creator")]
    public static void Open()
    {
        var w = GetWindow<ItemCreatorWindow>("Item Creator");
        w.minSize = new Vector2(370, 520);
        w.Show();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void OnEnable()
    {
        scriptDir   = EditorPrefs.GetString(PrefScriptDir,   DefaultScriptDir);
        dataDir     = EditorPrefs.GetString(PrefDataDir,     DefaultDataDir);
        prefabDir   = EditorPrefs.GetString(PrefPrefabDir,   DefaultPrefabDir);

        var itemPath    = EditorPrefs.GetString(PrefItemPrefab,    "");
        var dragPath    = EditorPrefs.GetString(PrefDragPrefab,    "");
        var interPath   = EditorPrefs.GetString(PrefInterPrefab,   "");
        if (!string.IsNullOrEmpty(itemPath))  itemBasePrefab        = AssetDatabase.LoadAssetAtPath<GameObject>(itemPath);
        if (!string.IsNullOrEmpty(dragPath))  draggableBasePrefab   = AssetDatabase.LoadAssetAtPath<GameObject>(dragPath);
        if (!string.IsNullOrEmpty(interPath)) interactableBasePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(interPath);
        defaultInteractionSprite = LoadSpriteFromPrefs(PrefDefaultSprite);

        RefreshItemId();

        EditorApplication.projectChanged += MarkExplorerDirty;
    }

    private void OnDisable()
    {
        SaveSettings();
        DisposeExplorer();
        EditorApplication.projectChanged -= MarkExplorerDirty;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        InitStyles();
        scroll = EditorGUILayout.BeginScrollView(scroll);

        DrawSettings();
        DrawSeparator();
        DrawNameField();
        EditorGUILayout.Space(4);
        DrawArchetypeButtons();
        EditorGUILayout.Space(4);
        DrawAbilities();
        EditorGUILayout.Space(4);
        DrawInteractionSprite();
        EditorGUILayout.Space(6);
        DrawAutoSection();
        EditorGUILayout.Space(8);
        DrawCreateButton();
        DrawExplorerSection();

        EditorGUILayout.EndScrollView();
    }

    // ── Settings section ──────────────────────────────────────────────────────
    private void DrawSettings()
    {
        settingsFoldout = EditorGUILayout.Foldout(settingsFoldout, "Settings", true, EditorStyles.foldoutHeader);
        if (!settingsFoldout) return;

        EditorGUI.indentLevel++;

        DrawPathField("Scripts", ref scriptDir, PrefScriptDir);
        DrawPathField("Data",    ref dataDir,   PrefDataDir);
        DrawPathField("Prefabs", ref prefabDir, PrefPrefabDir);

        EditorGUILayout.Space(2);

        DrawPrefabField("Item prefab",         ref itemBasePrefab,         PrefItemPrefab);
        DrawPrefabField("Draggable prefab",    ref draggableBasePrefab,    PrefDragPrefab);
        DrawPrefabField("Interactable prefab", ref interactableBasePrefab, PrefInterPrefab);

        EditorGUILayout.Space(2);
        DrawSpriteField("Default icon", ref defaultInteractionSprite, PrefDefaultSprite);

        EditorGUILayout.Space(4);
        if (GUILayout.Button("Open Viewmodel Preview Scene"))
            ViewmodelPreviewSceneCreator.OpenOrCreatePreviewScene();

        EditorGUI.indentLevel--;
    }

    private void DrawPathField(string label, ref string value, string prefsKey)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(label);
        var newVal = EditorGUILayout.TextField(value);
        if (newVal != value) { value = newVal; EditorPrefs.SetString(prefsKey, value); }
        if (GUILayout.Button("...", GUILayout.Width(28)))
        {
            var picked = EditorUtility.OpenFolderPanel("Select folder", value, "");
            if (!string.IsNullOrEmpty(picked))
            {
                // Convert absolute OS path to Assets-relative
                if (picked.StartsWith(Application.dataPath))
                    picked = "Assets" + picked.Substring(Application.dataPath.Length);
                value = picked;
                EditorPrefs.SetString(prefsKey, value);
                GUI.FocusControl(null);
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPrefabField(string label, ref GameObject value, string prefsKey)
    {
        EditorGUI.BeginChangeCheck();
        value = (GameObject)EditorGUILayout.ObjectField(label, value, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck())
            EditorPrefs.SetString(prefsKey, value != null ? AssetDatabase.GetAssetPath(value) : "");
    }

    private void DrawSpriteField(string label, ref Sprite value, string prefsKey)
    {
        EditorGUI.BeginChangeCheck();
        value = (Sprite)EditorGUILayout.ObjectField(label, value, typeof(Sprite), false);
        if (EditorGUI.EndChangeCheck())
            SaveSpriteToPrefs(prefsKey, value);
    }

    // Sprites are sub-assets of textures — path alone isn't enough to reload them.
    // Store "guid:localFileId" so we can round-trip any sprite.
    private static void SaveSpriteToPrefs(string key, Sprite sprite)
    {
        if (sprite == null) { EditorPrefs.DeleteKey(key); return; }
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long localId);
        EditorPrefs.SetString(key, $"{guid}:{localId}");
    }

    private static Sprite LoadSpriteFromPrefs(string key)
    {
        var stored = EditorPrefs.GetString(key, "");
        if (string.IsNullOrEmpty(stored)) return null;
        var parts = stored.Split(':');
        if (parts.Length != 2 || !long.TryParse(parts[1], out long localId)) return null;
        var path = AssetDatabase.GUIDToAssetPath(parts[0]);
        if (string.IsNullOrEmpty(path)) return null;
        return AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<Sprite>()
            .FirstOrDefault(s => {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(s, out _, out long id);
                return id == localId;
            });
    }

    // ── Name field ────────────────────────────────────────────────────────────
    private void DrawNameField()
    {
        EditorGUI.BeginChangeCheck();
        itemName = EditorGUILayout.TextField("Name", itemName);
        if (EditorGUI.EndChangeCheck() && archetype == Archetype.Item)
            RefreshItemId();
    }

    // ── Archetype buttons ─────────────────────────────────────────────────────
    private void DrawArchetypeButtons()
    {
        EditorGUILayout.LabelField("Archetype", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        DrawArchetypeButton("Item",         Archetype.Item);
        DrawArchetypeButton("Draggable",    Archetype.Draggable);
        DrawArchetypeButton("Interactable", Archetype.Interactable);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawArchetypeButton(string label, Archetype target)
    {
        bool active = archetype == target;
        var style = active ? _archetypeButtonActiveStyle : _archetypeButtonStyle;

        var prevBg = GUI.backgroundColor;
        if (active) GUI.backgroundColor = SelectionColor;
        bool clicked = GUILayout.Button(label, style);
        GUI.backgroundColor = prevBg;

        if (clicked && archetype != target)
        {
            archetype = target;
            ResetAbilities();
            if (target == Archetype.Item) RefreshItemId();
        }
    }

    // ── Abilities ─────────────────────────────────────────────────────────────
    private void DrawAbilities()
    {
        EditorGUILayout.LabelField("Abilities", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;

        switch (archetype)
        {
            case Archetype.Item:
                DrawItemAbilities();
                break;
            case Archetype.Draggable:
            case Archetype.Interactable:
                abilityRequiresItem = EditorGUILayout.Toggle("Requires Item", abilityRequiresItem);
                break;
        }

        EditorGUILayout.LabelField(GetResolvedBaseClass(), EditorStyles.miniLabel);
        EditorGUI.indentLevel--;
    }

    private void DrawItemAbilities()
    {
        EditorGUI.BeginChangeCheck();
        abilityUsable = EditorGUILayout.Toggle("Usable", abilityUsable);
        if (EditorGUI.EndChangeCheck() && !abilityUsable)
            abilityActivatable = false;

        using (new EditorGUI.DisabledScope(!abilityUsable))
        {
            abilityActivatable = EditorGUILayout.Toggle("Activatable", abilityUsable && abilityActivatable);
        }
    }

    private string GetResolvedBaseClass()
    {
        switch (archetype)
        {
            case Archetype.Item:
                if (!abilityUsable)        return "→ PassiveItem";
                if (!abilityActivatable)   return "→ UsableItem";
                return "→ ActivatableUsableItem";
            case Archetype.Draggable:
                return abilityRequiresItem ? "→ ItemInteractableDraggable" : "→ DraggableObject";
            case Archetype.Interactable:
                return abilityRequiresItem ? "→ ItemRequiredInteractable" : "→ InteractableObject";
        }
        return "";
    }

    // ── Interaction sprite ────────────────────────────────────────────────────
    private void DrawInteractionSprite()
    {
        interactionSprite = (Sprite)EditorGUILayout.ObjectField(
            "Interaction icon", interactionSprite, typeof(Sprite), false);
    }

    // ── Auto section ──────────────────────────────────────────────────────────
    private void DrawAutoSection()
    {
        autoFoldout = EditorGUILayout.Foldout(autoFoldout, "Auto", true, EditorStyles.foldoutHeader);
        if (!autoFoldout) return;

        EditorGUI.indentLevel++;
        using (new EditorGUI.DisabledScope(true))
        {
            var sub = GetSubfolder();
            EditorGUILayout.TextField("Script",  $"{sub}/{GetScriptName()}");
            EditorGUILayout.TextField("Data",    $"{sub}/{GetDataName()}");
            EditorGUILayout.TextField("Prefab",  $"{sub}/{GetPrefabName()}");
            if (archetype == Archetype.Item)
                EditorGUILayout.IntField("Item ID", nextItemId);
        }
        EditorGUI.indentLevel--;
    }

    // ── Create button ─────────────────────────────────────────────────────────
    private void DrawCreateButton()
    {
        var valid = ValidateInputs(out var error);

        using (new EditorGUI.DisabledScope(!valid))
        {
            if (GUILayout.Button("Create item", GUILayout.Height(36)))
                Create();
        }

        if (!valid)
            EditorGUILayout.HelpBox(error, MessageType.Warning);
    }

    // ── Validation ─────────────────────────────────────────────────────────────
    private bool ValidateInputs(out string error)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            error = "Enter an item name.";
            return false;
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(itemName, @"^[A-Za-z][A-Za-z0-9_]*$"))
        {
            error = "Name must be a valid C# identifier (letters, digits, underscores; start with a letter).";
            return false;
        }
        var scriptPath = GetScriptPath();
        if (File.Exists(scriptPath))
        {
            error = $"{GetScriptName()} already exists.";
            return false;
        }
        var basePrefab = GetBasePrefab();
        if (basePrefab == null)
        {
            error = $"Assign a base prefab for '{archetype}' in Settings.";
            return false;
        }
        error = null;
        return true;
    }

    // ── Creation logic ─────────────────────────────────────────────────────────
    private void Create()
    {
        var scriptPath = GetScriptPath();
        var dataPath   = GetDataPath();
        var prefabPath = GetPrefabPath();
        var className  = itemName;
        var baseClass  = GetBaseClassName();

        // 1. Create directories, then refresh so Unity registers them (creates .meta)
        EnsureDirectory(Path.GetDirectoryName(scriptPath));
        EnsureDirectory(Path.GetDirectoryName(dataPath));
        EnsureDirectory(Path.GetDirectoryName(prefabPath));
        AssetDatabase.Refresh();

        // 2. Create data asset (requires folders already registered above)
        CreateDataAsset(dataPath);

        // 3. Store pending prefab info BEFORE triggering compilation
        var pending = new ItemCreatorPostCompile.PendingItemData
        {
            ClassName      = className,
            BasePrefabPath = AssetDatabase.GetAssetPath(GetBasePrefab()),
            DataAssetPath  = dataPath,
            PrefabPath     = prefabPath
        };
        EditorPrefs.SetString(PendingKey, JsonUtility.ToJson(pending));

        // 4. Write script last — triggers domain reload
        File.WriteAllText(scriptPath, BuildTemplate(className, baseClass));
        AssetDatabase.ImportAsset(scriptPath, ImportAssetOptions.ForceUpdate);

        Debug.Log($"[ItemCreator] Script written: {scriptPath}. Prefab will be created after compilation.");
    }

    private ScriptableObject CreateDataAsset(string dataPath)
    {
        var icon = interactionSprite != null ? interactionSprite : defaultInteractionSprite;

        ScriptableObject asset;
        switch (archetype)
        {
            case Archetype.Item:
                var pickupData = ScriptableObject.CreateInstance<PickupItemData>();
                pickupData.ItemID = nextItemId;
                pickupData.InteractionSprite = icon;
                asset = pickupData;
                break;
            case Archetype.Draggable:
                var dragData = ScriptableObject.CreateInstance<DraggableObjectData>();
                dragData.InteractionSprite = icon;
                asset = dragData;
                break;
            default:
                var interData = ScriptableObject.CreateInstance<InteractableObjectData>();
                interData.InteractionSprite = icon;
                asset = interData;
                break;
        }
        AssetDatabase.CreateAsset(asset, dataPath);
        AssetDatabase.SaveAssets();
        return asset;
    }

    // ── Template builder ───────────────────────────────────────────────────────
    private string BuildTemplate(string className, string baseClass)
    {
        var methods = BuildMethodStubs(baseClass);
        return
$@"public class {className} : {baseClass}
{{
{methods}}}
";
    }

    private string BuildMethodStubs(string baseClass)
    {
        switch (baseClass)
        {
            case "PassiveItem":
                return
@"    public override void Activate()
    {
    }

";
            case "UsableItem":
                return
@"    public override void Use()
    {
    }

";
            case "ActivatableUsableItem":
                return
@"    public override void Use()
    {
    }

    public override void Activate()
    {
    }

";
            case "DraggableObject":
                return "";  // OnInteract fully implemented in base

            case "ItemInteractableDraggable":
                return
@"    protected override void InteractWith(IActivator activator)
    {
    }

    protected override void UnsuccessfulInteract()
    {
    }

";
            case "InteractableObject":
                return
@"    public override void OnInteract(InteractionContext context)
    {
    }

";
            case "ItemRequiredInteractable":
                return
@"    protected override void InteractWith(IActivator activator)
    {
    }

    protected override void UnsuccessfulInteract()
    {
    }

";
            default:
                return "";
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    private void RefreshItemId()
    {
        if (archetype != Archetype.Item) { nextItemId = -1; return; }
        var guids = AssetDatabase.FindAssets("t:PickupItemData");
        var usedIds = new HashSet<int>(guids
            .Select(g => AssetDatabase.LoadAssetAtPath<PickupItemData>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(a => a != null)
            .Select(a => a.ItemID));
        nextItemId = Enumerable.Range(1, int.MaxValue).First(id => !usedIds.Contains(id));
    }

    private void ResetAbilities()
    {
        abilityUsable = false;
        abilityActivatable = false;
        abilityRequiresItem = false;
    }

    private void SaveSettings()
    {
        EditorPrefs.SetString(PrefScriptDir,   scriptDir);
        EditorPrefs.SetString(PrefDataDir,     dataDir);
        EditorPrefs.SetString(PrefPrefabDir,   prefabDir);
        EditorPrefs.SetString(PrefItemPrefab,    itemBasePrefab           != null ? AssetDatabase.GetAssetPath(itemBasePrefab)           : "");
        EditorPrefs.SetString(PrefDragPrefab,    draggableBasePrefab      != null ? AssetDatabase.GetAssetPath(draggableBasePrefab)      : "");
        EditorPrefs.SetString(PrefInterPrefab,   interactableBasePrefab   != null ? AssetDatabase.GetAssetPath(interactableBasePrefab)   : "");
        SaveSpriteToPrefs(PrefDefaultSprite, defaultInteractionSprite);
    }

    private string GetBaseClassName()
    {
        switch (archetype)
        {
            case Archetype.Item:
                if (!abilityUsable)      return "PassiveItem";
                if (!abilityActivatable) return "UsableItem";
                return "ActivatableUsableItem";
            case Archetype.Draggable:
                return abilityRequiresItem ? "ItemInteractableDraggable" : "DraggableObject";
            default:
                return abilityRequiresItem ? "ItemRequiredInteractable" : "InteractableObject";
        }
    }

    private GameObject GetBasePrefab()
    {
        switch (archetype)
        {
            case Archetype.Item:        return itemBasePrefab;
            case Archetype.Draggable:   return draggableBasePrefab;
            default:                    return interactableBasePrefab;
        }
    }

    private string GetSubfolder()
    {
        switch (archetype)
        {
            case Archetype.Item:
                if (!abilityUsable)      return "Item/Passive";
                if (!abilityActivatable) return "Item/Usable";
                return "Item/ActivatableUsable";
            case Archetype.Draggable:
                return abilityRequiresItem ? "Draggable/ItemInteractable" : "Draggable/Base";
            default:
                return abilityRequiresItem ? "Interactable/ItemRequired" : "Interactable/Base";
        }
    }

    private string GetScriptName()  => string.IsNullOrWhiteSpace(itemName) ? "?.cs"       : $"{itemName}.cs";
    private string GetDataName()    => string.IsNullOrWhiteSpace(itemName) ? "?Data.asset" : $"{itemName}Data.asset";
    private string GetPrefabName()  => string.IsNullOrWhiteSpace(itemName) ? "?.prefab"    : $"{itemName}.prefab";
    private string GetScriptPath()  => $"{scriptDir}/{GetSubfolder()}/{GetScriptName()}";
    private string GetDataPath()    => $"{dataDir}/{GetSubfolder()}/{GetDataName()}";
    private string GetPrefabPath()  => $"{prefabDir}/{GetSubfolder()}/{GetPrefabName()}";

    private static void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
    }

    // ── Style init ─────────────────────────────────────────────────────────────
    private void InitStyles()
    {
        if (_stylesInitialized) return;
        _stylesInitialized = true;

        _archetypeButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontStyle = FontStyle.Normal,
            fixedHeight = 28
        };

        _archetypeButtonActiveStyle = new GUIStyle(GUI.skin.button)
        {
            fontStyle = FontStyle.Bold,
            fixedHeight = 28
        };

        _headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
    }

}
