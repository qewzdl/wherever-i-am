using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class GameMapManagerWindow : EditorWindow
{
    private const string DefaultCatalogPath =
        "Assets/Collaborators/Qewzdl/Configs/Maps/GameMapCatalog.asset";

    [SerializeField] private GameMapCatalog catalog;
    [SerializeField] private string newMapName = "New Map";
    [SerializeField] private Vector2 scrollPosition;

    private readonly List<MapValidationEntry> validationEntries = new List<MapValidationEntry>();

    // Definitions that exist as assets and are in no catalogue.
    //
    // A map that is not registered is not half-registered: the game cannot
    // load it, the manager did not list it, and nothing anywhere said it was
    // there. The only way in was to make a new one or to duplicate one that
    // was already in - so a definition that arrived any other way, from a
    // branch, from a copy, from a catalogue entry somebody removed, was gone
    // as far as the tools were concerned while sitting in plain sight in the
    // project window.
    private readonly List<GameMapDefinition> unregistered = new List<GameMapDefinition>();
    private string catalogError;

    // The world group: what the game is made of. See ProjectSceneMenu for the
    // scheme the numbers follow.
    [MenuItem("Tools/Wherever I Am/Map Manager", false, 100)]
    private static void OpenWindow()
    {
        GameMapManagerWindow window = GetWindow<GameMapManagerWindow>("Map Manager");
        window.minSize = new Vector2(680f, 420f);
        window.Show();
    }

    private void OnEnable()
    {
        if (catalog == null)
            catalog = AssetDatabase.LoadAssetAtPath<GameMapCatalog>(DefaultCatalogPath);

        RefreshValidation();
    }

    private void OnFocus()
    {
        RefreshValidation();
        Repaint();
    }

    private void OnGUI()
    {
        DrawHeader();

        if (catalog == null)
        {
            EditorGUILayout.HelpBox(
                $"Assign a {nameof(GameMapCatalog)}. The default catalog was not found at '{DefaultCatalogPath}'.",
                MessageType.Error);
            return;
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        DrawCatalogStatus();
        DrawDefaultMapSelector();
        DrawCreateSection();
        DrawUnregisteredSection();
        DrawMapList();
        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUI.BeginChangeCheck();
        GameMapCatalog nextCatalog = (GameMapCatalog)EditorGUILayout.ObjectField(
            catalog,
            typeof(GameMapCatalog),
            false,
            GUILayout.MinWidth(220f));

        if (EditorGUI.EndChangeCheck())
        {
            catalog = nextCatalog;
            RefreshValidation();
        }

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            RefreshValidation();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawCatalogStatus()
    {
        if (!string.IsNullOrWhiteSpace(catalogError))
            EditorGUILayout.HelpBox(catalogError, MessageType.Error);
        else
            EditorGUILayout.HelpBox(
                $"{catalog.Count} map(s) registered. Catalog configuration is valid.",
                MessageType.Info);
    }

    private void DrawDefaultMapSelector()
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Default Map", EditorStyles.boldLabel);

        List<GameMapDefinition> availableMaps = GetAvailableMaps();

        if (availableMaps.Count == 0)
        {
            EditorGUILayout.HelpBox("The catalog has no map definitions.", MessageType.Warning);
            return;
        }

        string[] options = new string[availableMaps.Count];
        int selectedIndex = 0;

        for (int i = 0; i < availableMaps.Count; i++)
        {
            GameMapDefinition map = availableMaps[i];
            options[i] = $"[{map.MapId}] {map.DisplayName}";

            if (map.MapId == catalog.DefaultMapId)
                selectedIndex = i;
        }

        EditorGUI.BeginChangeCheck();
        int nextIndex = EditorGUILayout.Popup("Active By Default", selectedIndex, options);

        if (EditorGUI.EndChangeCheck())
            SetDefaultMap(availableMaps[nextIndex]);
    }

    private void DrawCreateSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Create Map", EditorStyles.boldLabel);

        int nextMapId = catalog.GetNextAvailableMapIdEditor();

        using (new EditorGUILayout.HorizontalScope())
        {
            newMapName = EditorGUILayout.TextField("Display Name", newMapName);

            using (new EditorGUI.DisabledScope(true))
            using (new EditorGUILayout.HorizontalScope(GUILayout.Width(130f)))
            {
                EditorGUILayout.LabelField("Map Id", GUILayout.Width(48f));
                EditorGUILayout.IntField(nextMapId, GUILayout.Width(74f));
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(newMapName)))
            {
                if (GUILayout.Button("Create", GUILayout.Width(90f)))
                    CreateMap(nextMapId, newMapName);
            }
        }
    }

    private void DrawUnregisteredSection()
    {
        if (unregistered.Count == 0)
            return;

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Not In The Catalog", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            unregistered.Count == 1
                ? "One map definition exists in the project and is not registered. The game cannot load it until it is."
                : $"{unregistered.Count} map definitions exist in the project and are not registered. The game cannot load them until they are.",
            MessageType.Info);

        for (int i = 0; i < unregistered.Count; i++)
            DrawUnregisteredEntry(unregistered[i]);
    }

    private void DrawUnregisteredEntry(GameMapDefinition definition)
    {
        if (definition == null)
            return;

        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            string name = string.IsNullOrWhiteSpace(definition.DisplayName)
                ? definition.name
                : definition.DisplayName;

            EditorGUILayout.LabelField(name, EditorStyles.boldLabel);

            // The id it is carrying, and whether it can keep it. A definition
            // copied from another branch usually cannot: the number it was
            // given there belongs to something else here.
            bool idIsFree = !catalog.TryGetMap(definition.MapId, out _) &&
                            definition.MapId >= 0;

            EditorGUILayout.LabelField(
                idIsFree
                    ? $"Id {definition.MapId}"
                    : $"Id {definition.MapId} taken, will become {catalog.GetNextAvailableMapIdEditor()}",
                GUILayout.Width(240f));

            if (GUILayout.Button("Ping", GUILayout.Width(48f)))
                EditorGUIUtility.PingObject(definition);

            if (GUILayout.Button("Add", GUILayout.Width(58f)))
                AddExistingMap(definition);
        }
    }

    // Registering something that is already there, which is a different job
    // from making one: the scene and the definition exist and are not touched
    // beyond the id, and the id is only touched when it has to be.
    private void AddExistingMap(GameMapDefinition definition)
    {
        if (definition == null || catalog == null)
            return;

        int mapId = definition.MapId;

        if (mapId < 0 || catalog.TryGetMap(mapId, out _))
        {
            mapId = catalog.GetNextAvailableMapIdEditor();

            Undo.RecordObject(definition, "Renumber Game Map");
            definition.ConfigureEditor(
                mapId,
                definition.DisplayName,
                definition.SceneName,
                definition.ScenePath);

            EditorUtility.SetDirty(definition);
        }

        Undo.RecordObject(catalog, "Register Game Map");

        if (!catalog.AddMapEditor(definition))
        {
            EditorUtility.DisplayDialog(
                "Add Game Map",
                $"'{definition.name}' could not be registered under id {mapId}.",
                "OK");

            return;
        }

        EditorUtility.SetDirty(catalog);

        // A map the game cannot load is not registered in any useful sense,
        // and it cannot load a scene that is not in the build.
        if (!string.IsNullOrWhiteSpace(definition.ScenePath))
            GameMapEditorUtility.EnsureSceneInBuildSettings(definition.ScenePath);

        AssetDatabase.SaveAssets();
        RefreshValidation();

        Selection.activeObject = definition;
        EditorGUIUtility.PingObject(definition);
    }

    private void DrawMapList()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Maps", EditorStyles.boldLabel);

        if (validationEntries.Count == 0)
        {
            EditorGUILayout.HelpBox("No maps are registered.", MessageType.Warning);
            return;
        }

        for (int i = 0; i < validationEntries.Count; i++)
            DrawMapEntry(validationEntries[i], i);
    }

    private void DrawMapEntry(MapValidationEntry entry, int catalogIndex)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        if (entry.Map == null)
        {
            EditorGUILayout.HelpBox("The catalog contains a missing map reference.", MessageType.Error);

            if (GUILayout.Button("Remove Missing Catalog Entry"))
                RemoveMissingMapEntry(catalogIndex);

            EditorGUILayout.EndVertical();
            return;
        }

        GameMapDefinition map = entry.Map;

        using (new EditorGUILayout.HorizontalScope())
        {
            string defaultSuffix = map.MapId == catalog.DefaultMapId ? "  (DEFAULT)" : string.Empty;
            EditorGUILayout.LabelField(
                $"[{map.MapId}] {map.DisplayName}{defaultSuffix}",
                EditorStyles.boldLabel);

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(map.MapId == catalog.DefaultMapId))
            {
                if (GUILayout.Button("Set Default", GUILayout.Width(86f)))
                    SetDefaultMap(map);
            }

            using (new EditorGUI.DisabledScope(!entry.SceneExists))
            {
                if (GUILayout.Button("Open", GUILayout.Width(58f)))
                    GameMapEditorUtility.OpenMapOnly(map);

                if (GUILayout.Button("Open With Game", GUILayout.Width(112f)))
                    GameMapEditorUtility.OpenWithGame(map);
            }

            if (GUILayout.Button("Duplicate", GUILayout.Width(74f)))
                DuplicateMap(map);

            if (GUILayout.Button("Delete", GUILayout.Width(58f)))
                DeleteMap(map);
        }

        EditorGUILayout.ObjectField("Definition", map, typeof(GameMapDefinition), false);
        EditorGUILayout.LabelField("Scene", map.ScenePath);

        if (entry.Errors.Count == 0)
        {
            EditorGUILayout.HelpBox("Configuration is valid.", MessageType.Info);
        }
        else
        {
            for (int i = 0; i < entry.Errors.Count; i++)
                EditorGUILayout.HelpBox(entry.Errors[i], MessageType.Error);
        }

        if (entry.SceneExists && !entry.SceneEnabledInBuildSettings)
        {
            if (GUILayout.Button("Add And Enable Scene In Build Settings"))
            {
                GameMapEditorUtility.EnsureSceneInBuildSettings(map.ScenePath);
                AssetDatabase.SaveAssets();
                RefreshValidation();
            }
        }

        EditorGUILayout.EndVertical();
    }

    private List<GameMapDefinition> GetAvailableMaps()
    {
        List<GameMapDefinition> availableMaps = new List<GameMapDefinition>();

        for (int i = 0; i < catalog.Count; i++)
        {
            GameMapDefinition map = catalog.GetMapAt(i);

            if (map != null)
                availableMaps.Add(map);
        }

        return availableMaps;
    }

    private void SetDefaultMap(GameMapDefinition map)
    {
        if (map == null)
            return;

        MapValidationEntry validationEntry = validationEntries.Find(entry => entry.Map == map);

        if (validationEntry != null && validationEntry.Errors.Count > 0)
        {
            EditorUtility.DisplayDialog(
                "Set Default Game Map",
                $"Map '{map.DisplayName}' has configuration errors and cannot be the default map.",
                "OK");
            return;
        }

        Undo.RecordObject(catalog, "Set Default Game Map");

        if (!catalog.SetDefaultMapEditor(map.MapId))
            return;

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        RefreshValidation();
    }

    private void CreateMap(int mapId, string displayName)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        string sanitizedName = GameMapEditorUtility.SanitizeFileName(displayName);

        if (string.IsNullOrWhiteSpace(sanitizedName))
        {
            EditorUtility.DisplayDialog("Create Game Map", "Map name is not valid.", "OK");
            return;
        }

        GameMapEditorUtility.EnsureMapFolders();

        string scenePath = AssetDatabase.GenerateUniqueAssetPath(
            $"{GameMapEditorUtility.MapsFolder}/Map_{sanitizedName}.unity");

        string definitionPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{GameMapEditorUtility.DefinitionsFolder}/Map_{sanitizedName}.asset");

        bool catalogChanged = false;

        try
        {
            GameMapEditorUtility.CreateEmptyMapScene(scenePath, sanitizedName);

            GameMapDefinition definition = CreateInstance<GameMapDefinition>();
            definition.ConfigureEditor(
                mapId,
                displayName.Trim(),
                Path.GetFileNameWithoutExtension(scenePath),
                scenePath);

            AssetDatabase.CreateAsset(definition, definitionPath);

            Undo.RecordObject(catalog, "Register Game Map");

            if (!catalog.AddMapEditor(definition))
                throw new InvalidOperationException($"Map id {mapId} is already registered.");

            catalogChanged = true;
            EditorUtility.SetDirty(catalog);
            GameMapEditorUtility.EnsureSceneInBuildSettings(scenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            newMapName = "New Map";
            Selection.activeObject = definition;
            EditorGUIUtility.PingObject(definition);
            RefreshValidation();
        }
        catch (Exception exception)
        {
            if (catalogChanged)
            {
                catalog.RemoveMapEditor(mapId);
                EditorUtility.SetDirty(catalog);
            }

            GameMapEditorUtility.RemoveSceneFromBuildSettings(scenePath);
            AssetDatabase.DeleteAsset(definitionPath);
            AssetDatabase.DeleteAsset(scenePath);
            AssetDatabase.SaveAssets();
            RefreshValidation();

            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Create Game Map", exception.Message, "OK");
        }
    }

    private void DuplicateMap(GameMapDefinition source)
    {
        if (source == null || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        if (!GameMapEditorUtility.SceneAssetExists(source.ScenePath))
        {
            EditorUtility.DisplayDialog(
                "Duplicate Game Map",
                $"Source scene '{source.ScenePath}' does not exist.",
                "OK");
            return;
        }

        string sourceDefinitionPath = AssetDatabase.GetAssetPath(source);

        if (string.IsNullOrWhiteSpace(sourceDefinitionPath))
        {
            EditorUtility.DisplayDialog(
                "Duplicate Game Map",
                "The source map definition is not a project asset.",
                "OK");
            return;
        }

        int mapId = catalog.GetNextAvailableMapIdEditor();
        string displayName = $"{source.DisplayName} Copy";
        string sanitizedName = GameMapEditorUtility.SanitizeFileName(displayName);

        GameMapEditorUtility.EnsureMapFolders();

        string scenePath = AssetDatabase.GenerateUniqueAssetPath(
            $"{GameMapEditorUtility.MapsFolder}/Map_{sanitizedName}.unity");

        string definitionPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{GameMapEditorUtility.DefinitionsFolder}/Map_{sanitizedName}.asset");

        bool catalogChanged = false;

        try
        {
            if (!AssetDatabase.CopyAsset(source.ScenePath, scenePath))
                throw new InvalidOperationException($"Failed to copy scene to '{scenePath}'.");

            if (!AssetDatabase.CopyAsset(sourceDefinitionPath, definitionPath))
                throw new InvalidOperationException($"Failed to copy map definition to '{definitionPath}'.");

            AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(definitionPath, ImportAssetOptions.ForceSynchronousImport);
            GameMapEditorUtility.ReserializeCopiedMapScene(scenePath);

            GameMapDefinition duplicate =
                AssetDatabase.LoadAssetAtPath<GameMapDefinition>(definitionPath);

            if (duplicate == null)
                throw new InvalidOperationException("Failed to load the duplicated map definition.");

            duplicate.ConfigureEditor(
                mapId,
                displayName,
                Path.GetFileNameWithoutExtension(scenePath),
                scenePath);

            EditorUtility.SetDirty(duplicate);
            Undo.RecordObject(catalog, "Duplicate Game Map");

            if (!catalog.AddMapEditor(duplicate))
                throw new InvalidOperationException($"Map id {mapId} is already registered.");

            catalogChanged = true;
            EditorUtility.SetDirty(catalog);
            GameMapEditorUtility.EnsureSceneInBuildSettings(scenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = duplicate;
            EditorGUIUtility.PingObject(duplicate);
            RefreshValidation();
        }
        catch (Exception exception)
        {
            if (catalogChanged)
            {
                catalog.RemoveMapEditor(mapId);
                EditorUtility.SetDirty(catalog);
            }

            GameMapEditorUtility.RemoveSceneFromBuildSettings(scenePath);
            AssetDatabase.DeleteAsset(definitionPath);
            AssetDatabase.DeleteAsset(scenePath);
            AssetDatabase.SaveAssets();
            RefreshValidation();

            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Duplicate Game Map", exception.Message, "OK");
        }
    }

    private void DeleteMap(GameMapDefinition map)
    {
        if (map == null)
            return;

        if (catalog.Count <= 1)
        {
            EditorUtility.DisplayDialog(
                "Delete Game Map",
                "The last map cannot be deleted. Create another map first.",
                "OK");
            return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "Delete Game Map",
            $"Delete map [{map.MapId}] {map.DisplayName}?\n\n" +
            "Its scene and definition asset will be moved to the system trash.",
            "Delete",
            "Cancel");

        if (!confirmed || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        int mapId = map.MapId;
        int catalogIndex = GetCatalogIndex(map);
        string scenePath = map.ScenePath;
        string definitionPath = AssetDatabase.GetAssetPath(map);

        if (catalogIndex < 0)
        {
            EditorUtility.DisplayDialog(
                "Delete Game Map",
                "The selected map is no longer registered in the catalog.",
                "OK");
            RefreshValidation();
            return;
        }

        GameMapEditorUtility.CloseSceneBeforeDelete(scenePath);

        bool sceneRemoved = !GameMapEditorUtility.SceneAssetExists(scenePath) ||
                            AssetDatabase.MoveAssetToTrash(scenePath);

        bool definitionRemoved = string.IsNullOrWhiteSpace(definitionPath) ||
                                 AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(definitionPath) == null ||
                                 AssetDatabase.MoveAssetToTrash(definitionPath);

        if (!sceneRemoved || !definitionRemoved)
        {
            AssetDatabase.Refresh();
            RefreshValidation();
            EditorUtility.DisplayDialog(
                "Delete Game Map",
                "Unity could not move all map assets to the system trash. " +
                "The catalog entry was kept so the configuration can be repaired.",
                "OK");
            return;
        }

        Undo.RecordObject(catalog, "Delete Game Map");

        if (!catalog.RemoveMapAtEditor(catalogIndex))
        {
            EditorUtility.DisplayDialog(
                "Delete Game Map",
                $"Map id {mapId} could not be removed from the catalog.",
                "OK");
            return;
        }

        EditorUtility.SetDirty(catalog);
        GameMapEditorUtility.RemoveSceneFromBuildSettings(scenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        RefreshValidation();
    }

    private void RemoveMissingMapEntry(int catalogIndex)
    {
        Undo.RecordObject(catalog, "Remove Missing Game Map Entry");

        if (!catalog.RemoveMapAtEditor(catalogIndex))
            return;

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        RefreshValidation();
    }

    private int GetCatalogIndex(GameMapDefinition map)
    {
        for (int i = 0; i < catalog.Count; i++)
        {
            if (catalog.GetMapAt(i) == map)
                return i;
        }

        return -1;
    }

    private void RefreshValidation()
    {
        validationEntries.Clear();
        catalogError = string.Empty;

        if (catalog == null)
            return;

        if (!catalog.IsValid(out catalogError))
            catalogError = $"Catalog: {catalogError}";

        ObjectiveSequenceDefinition defaultObjectiveSequence =
            GameMapEditorUtility.LoadDefaultObjectiveSequence();

        for (int i = 0; i < catalog.Count; i++)
        {
            validationEntries.Add(
                GameMapEditorUtility.Validate(
                    catalog.GetMapAt(i),
                    defaultObjectiveSequence));
        }

        RefreshUnregistered();
    }

    // Every definition in the project, minus the ones this catalogue already
    // holds. Read off disk rather than remembered, for the same reason the
    // rest of this window is: the assets are made and moved outside it.
    private void RefreshUnregistered()
    {
        List<GameMapDefinition> all = new();

        foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(GameMapDefinition)}"))
        {
            GameMapDefinition definition =
                AssetDatabase.LoadAssetAtPath<GameMapDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));

            if (definition != null)
                all.Add(definition);
        }

        CollectUnregistered(catalog, all, unregistered);
    }

    /// <summary>
    /// Which of these the catalog does not hold.
    /// </summary>
    /// <remarks>
    /// Told what to look at rather than going and finding it, so the one
    /// decision worth getting right can be tested without a project full of
    /// assets around it.
    ///
    /// Compared by asset and not by id on purpose: two definitions can carry
    /// the same number - that is exactly what a copy from another branch does -
    /// and the one the catalog actually holds is the one that counts. By id,
    /// the copy would look registered and quietly never be.
    /// </remarks>
    public static void CollectUnregistered(
        GameMapCatalog catalog,
        IReadOnlyList<GameMapDefinition> all,
        List<GameMapDefinition> into)
    {
        into.Clear();

        if (catalog == null || all == null)
            return;

        for (int i = 0; i < all.Count; i++)
        {
            GameMapDefinition definition = all[i];

            if (definition == null || Registered(catalog, definition))
                continue;

            into.Add(definition);
        }
    }

    private static bool Registered(GameMapCatalog catalog, GameMapDefinition definition)
    {
        for (int i = 0; i < catalog.Count; i++)
        {
            if (catalog.GetMapAt(i) == definition)
                return true;
        }

        return false;
    }
}

internal sealed class MapValidationEntry
{
    public MapValidationEntry(GameMapDefinition map)
    {
        Map = map;
    }

    public GameMapDefinition Map { get; }
    public List<string> Errors { get; } = new List<string>();
    public bool SceneExists { get; set; }
    public bool SceneEnabledInBuildSettings { get; set; }
}

internal static class GameMapEditorUtility
{
    public const string MapsFolder =
        "Assets/Collaborators/Qewzdl/Scenes/Maps";

    public const string DefinitionsFolder =
        "Assets/Collaborators/Qewzdl/Configs/Maps/Definitions";

    private const string GameScenePath =
        "Assets/Collaborators/Qewzdl/Scenes/Game.unity";

    public static void OpenMapOnly(GameMapDefinition map)
    {
        if (!CanOpenMap(map) || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EditorSceneManager.OpenScene(map.ScenePath, OpenSceneMode.Single);
    }

    public static void OpenWithGame(GameMapDefinition map)
    {
        if (!CanOpenMap(map) || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        if (!SceneAssetExists(GameScenePath))
        {
            EditorUtility.DisplayDialog(
                "Open Map With Game",
                $"Game scene '{GameScenePath}' does not exist.",
                "OK");
            return;
        }

        EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        Scene mapScene = EditorSceneManager.OpenScene(map.ScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(mapScene);
    }

    public static void CreateEmptyMapScene(string scenePath, string sanitizedName)
    {
        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
        Scene mapScene = default;

        try
        {
            mapScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);

            GameObject mapRootObject = new GameObject($"Map_{sanitizedName}");
            GameMapRoot mapRoot = mapRootObject.AddComponent<GameMapRoot>();
            SceneManager.MoveGameObjectToScene(mapRootObject, mapScene);

            GameObject spawnPointsRoot = new GameObject("Player Spawn Points");
            spawnPointsRoot.transform.SetParent(mapRootObject.transform);

            GameObject firstSpawn = new GameObject("PlayerSpawn_01");
            firstSpawn.transform.SetParent(spawnPointsRoot.transform);
            firstSpawn.transform.localPosition = Vector3.up;

            GameObject objectivesRoot = new GameObject("Map Objectives");
            objectivesRoot.transform.SetParent(mapRootObject.transform);

            ObjectiveSceneBindingRegistry bindingRegistry =
                objectivesRoot.AddComponent<ObjectiveSceneBindingRegistry>();
            bindingRegistry.ConfigureEditor(Array.Empty<ObjectiveSceneBinding>());

            mapRoot.ConfigureEditor(
                new[] { firstSpawn.transform },
                bindingRegistry);

            if (!EditorSceneManager.SaveScene(mapScene, scenePath))
                throw new InvalidOperationException($"Failed to save map scene at '{scenePath}'.");
        }
        finally
        {
            if (mapScene.IsValid() && mapScene.isLoaded)
                EditorSceneManager.CloseScene(mapScene, true);

            EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
        }
    }

    public static void ReserializeCopiedMapScene(string scenePath)
    {
        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
        Scene mapScene = default;

        try
        {
            mapScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

            if (!EditorSceneManager.SaveScene(mapScene))
            {
                throw new InvalidOperationException(
                    $"Failed to refresh scene metadata at '{scenePath}'.");
            }
        }
        finally
        {
            if (mapScene.IsValid() && mapScene.isLoaded)
                EditorSceneManager.CloseScene(mapScene, true);

            EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
        }
    }

    public static MapValidationEntry Validate(
        GameMapDefinition map,
        ObjectiveSequenceDefinition defaultObjectiveSequence)
    {
        MapValidationEntry entry = new MapValidationEntry(map);

        if (map == null)
        {
            entry.Errors.Add("The map definition reference is missing.");
            return entry;
        }

        if (!map.IsConfigured(out string configurationError))
            entry.Errors.Add(configurationError);

        string definitionPath = AssetDatabase.GetAssetPath(map);

        if (string.IsNullOrWhiteSpace(definitionPath))
            entry.Errors.Add("The map definition is not saved as a project asset.");

        entry.SceneExists = SceneAssetExists(map.ScenePath);

        if (!entry.SceneExists)
        {
            entry.Errors.Add($"Scene asset '{map.ScenePath}' does not exist.");
            return entry;
        }

        string actualSceneName = Path.GetFileNameWithoutExtension(map.ScenePath);

        if (!string.Equals(actualSceneName, map.SceneName, StringComparison.Ordinal))
        {
            entry.Errors.Add(
                $"Scene name mismatch: definition uses '{map.SceneName}', " +
                $"but path points to '{actualSceneName}'.");
        }

        entry.SceneEnabledInBuildSettings = IsSceneEnabledInBuildSettings(map.ScenePath);

        if (!entry.SceneEnabledInBuildSettings)
            entry.Errors.Add("Scene is missing or disabled in Build Settings.");

        ObjectiveSequenceDefinition activeSequence =
            map.ObjectiveSequenceOverride != null
                ? map.ObjectiveSequenceOverride
                : defaultObjectiveSequence;

        if (activeSequence == null)
            entry.Errors.Add("No objective sequence is configured for this map.");

        ValidateSceneContents(map.ScenePath, activeSequence, entry.Errors);
        return entry;
    }

    public static ObjectiveSequenceDefinition LoadDefaultObjectiveSequence()
    {
        if (!SceneAssetExists(GameScenePath))
            return null;

        Scene scene = SceneManager.GetSceneByPath(GameScenePath);
        bool closePreviewScene = false;

        try
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenPreviewScene(GameScenePath);
                closePreviewScene = true;
            }

            GameObject[] rootObjects = scene.GetRootGameObjects();

            for (int i = 0; i < rootObjects.Length; i++)
            {
                NetworkObjectiveFlow flow =
                    rootObjects[i].GetComponentInChildren<NetworkObjectiveFlow>(true);

                if (flow != null)
                    return flow.DefaultObjectiveSequenceEditor;
            }

            return null;
        }
        finally
        {
            if (closePreviewScene && scene.IsValid())
                EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    public static bool SceneAssetExists(string scenePath)
    {
        return !string.IsNullOrWhiteSpace(scenePath) &&
               AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null;
    }

    public static bool EnsureSceneInBuildSettings(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
            return false;

        List<EditorBuildSettingsScene> scenes =
            new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

        for (int i = 0; i < scenes.Count; i++)
        {
            if (!PathsEqual(scenes[i].path, scenePath))
                continue;

            if (scenes[i].enabled)
                return false;

            scenes[i] = new EditorBuildSettingsScene(scenePath, true);
            EditorBuildSettings.scenes = scenes.ToArray();
            return true;
        }

        scenes.Add(new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        return true;
    }

    public static bool RemoveSceneFromBuildSettings(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
            return false;

        List<EditorBuildSettingsScene> scenes =
            new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

        int removedCount = scenes.RemoveAll(scene => PathsEqual(scene.path, scenePath));

        if (removedCount == 0)
            return false;

        EditorBuildSettings.scenes = scenes.ToArray();
        return true;
    }

    public static void EnsureMapFolders()
    {
        EnsureFolder(MapsFolder);
        EnsureFolder(DefinitionsFolder);
    }

    public static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string sanitized = value.Trim();

        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            sanitized = sanitized.Replace(invalidCharacter, '_');

        return sanitized.Replace(' ', '_');
    }

    public static void CloseSceneBeforeDelete(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
            return;

        Scene scene = SceneManager.GetSceneByPath(scenePath);

        if (!scene.IsValid() || !scene.isLoaded)
            return;

        if (SceneManager.sceneCount <= 1)
        {
            if (SceneAssetExists(GameScenePath))
                EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            else
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            return;
        }

        EditorSceneManager.CloseScene(scene, true);
    }

    private static bool CanOpenMap(GameMapDefinition map)
    {
        if (map != null && SceneAssetExists(map.ScenePath))
            return true;

        EditorUtility.DisplayDialog(
            "Open Game Map",
            map == null
                ? "The map definition is missing."
                : $"Scene '{map.ScenePath}' does not exist.",
            "OK");

        return false;
    }

    private static bool IsSceneEnabledInBuildSettings(string scenePath)
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;

        for (int i = 0; i < scenes.Length; i++)
        {
            if (PathsEqual(scenes[i].path, scenePath))
                return scenes[i].enabled;
        }

        return false;
    }

    private static void ValidateSceneContents(
        string scenePath,
        ObjectiveSequenceDefinition activeSequence,
        List<string> errors)
    {
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool closePreviewScene = false;

        try
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenPreviewScene(scenePath);
                closePreviewScene = true;
            }

            List<GameMapRoot> mapRoots = new List<GameMapRoot>();
            GameObject[] rootObjects = scene.GetRootGameObjects();

            for (int i = 0; i < rootObjects.Length; i++)
            {
                GameMapRoot[] roots =
                    rootObjects[i].GetComponentsInChildren<GameMapRoot>(true);
                mapRoots.AddRange(roots);
            }

            if (mapRoots.Count == 0)
            {
                errors.Add($"Scene '{scenePath}' has no {nameof(GameMapRoot)}.");
                return;
            }

            if (mapRoots.Count > 1)
            {
                errors.Add(
                    $"Scene '{scenePath}' has {mapRoots.Count} {nameof(GameMapRoot)} components. " +
                    "Exactly one is required.");
                return;
            }

            if (mapRoots[0].PlayerSpawnPointCount == 0)
                errors.Add($"{nameof(GameMapRoot)} has no player spawn points.");

            ObjectiveSceneBindingRegistry bindingRegistry =
                mapRoots[0].ObjectiveBindingRegistry;

            if (bindingRegistry == null)
            {
                errors.Add(
                    $"{nameof(GameMapRoot)} has no assigned " +
                    $"{nameof(ObjectiveSceneBindingRegistry)}.");
                return;
            }

            if (activeSequence != null &&
                !bindingRegistry.IsValidForSequence(activeSequence, out string bindingError))
            {
                errors.Add(bindingError);
            }
        }
        catch (Exception exception)
        {
            errors.Add($"Failed to inspect scene '{scenePath}': {exception.Message}");
        }
        finally
        {
            if (closePreviewScene && scene.IsValid())
                EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void EnsureFolder(string folderPath)
    {
        string normalizedPath = folderPath.Replace('\\', '/');

        if (AssetDatabase.IsValidFolder(normalizedPath))
            return;

        string[] parts = normalizedPath.Split('/');
        string currentPath = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string nextPath = $"{currentPath}/{parts[i]}";

            if (!AssetDatabase.IsValidFolder(nextPath))
                AssetDatabase.CreateFolder(currentPath, parts[i]);

            currentPath = nextPath;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            left?.Replace('\\', '/'),
            right?.Replace('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
    }
}

[CustomEditor(typeof(GameMapDefinition))]
public sealed class GameMapDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        GameMapDefinition map = (GameMapDefinition)target;

        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(map.ScenePath)))
        {
            if (GUILayout.Button("Open Map Only"))
                GameMapEditorUtility.OpenMapOnly(map);

            if (GUILayout.Button("Open With Game"))
                GameMapEditorUtility.OpenWithGame(map);
        }

        if (GUILayout.Button("Open Map Manager"))
            EditorWindow.GetWindow<GameMapManagerWindow>("Map Manager");
    }
}
