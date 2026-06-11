using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class GameMapCreatorWindow : EditorWindow
{
    private const string DefaultCatalogPath =
        "Assets/Collaborators/Qewzdl/Configs/Maps/GameMapCatalog.asset";

    private const string DefaultMapsFolder =
        "Assets/Collaborators/Qewzdl/Scenes/Maps";

    private const string DefaultDefinitionsFolder =
        "Assets/Collaborators/Qewzdl/Configs/Maps/Definitions";

    [SerializeField] private GameMapCatalog catalog;
    [SerializeField] private int mapId;
    [SerializeField] private string mapName = "New Map";

    [MenuItem("Wherever I Am/Maps/Create Map")]
    private static void OpenWindow()
    {
        GetWindow<GameMapCreatorWindow>("Create Game Map");
    }

    private void OnEnable()
    {
        if (catalog == null)
            catalog = AssetDatabase.LoadAssetAtPath<GameMapCatalog>(DefaultCatalogPath);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Map Definition", EditorStyles.boldLabel);
        catalog = (GameMapCatalog)EditorGUILayout.ObjectField(
            "Catalog",
            catalog,
            typeof(GameMapCatalog),
            false);

        mapId = Mathf.Max(0, EditorGUILayout.IntField("Map Id", mapId));
        mapName = EditorGUILayout.TextField("Display Name", mapName);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(!CanCreate()))
        {
            if (GUILayout.Button("Create Map Scene", GUILayout.Height(32f)))
                CreateMap();
        }
    }

    private bool CanCreate()
    {
        return catalog != null &&
               !string.IsNullOrWhiteSpace(mapName) &&
               !catalog.TryGetMap(mapId, out _);
    }

    private void CreateMap()
    {
        string fileName = SanitizeFileName(mapName);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            EditorUtility.DisplayDialog("Create Game Map", "Map name is not valid.", "OK");
            return;
        }

        EnsureFolder(DefaultMapsFolder);
        EnsureFolder(DefaultDefinitionsFolder);

        string scenePath = AssetDatabase.GenerateUniqueAssetPath(
            $"{DefaultMapsFolder}/Map_{fileName}.unity");

        string definitionPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{DefaultDefinitionsFolder}/Map_{fileName}.asset");

        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
        Scene mapScene = default;

        try
        {
            mapScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);

            GameObject mapRootObject = new GameObject($"Map_{fileName}");
            GameMapRoot mapRoot = mapRootObject.AddComponent<GameMapRoot>();
            SceneManager.MoveGameObjectToScene(mapRootObject, mapScene);

            GameObject spawnPointsRoot = new GameObject("Player Spawn Points");
            spawnPointsRoot.transform.SetParent(mapRootObject.transform);

            GameObject firstSpawn = new GameObject("PlayerSpawn_01");
            firstSpawn.transform.SetParent(spawnPointsRoot.transform);
            firstSpawn.transform.localPosition = Vector3.up;

            mapRoot.ConfigureEditor(new[] { firstSpawn.transform });

            if (!EditorSceneManager.SaveScene(mapScene, scenePath))
                throw new InvalidOperationException($"Failed to save map scene at '{scenePath}'.");
        }
        finally
        {
            if (mapScene.IsValid() && mapScene.isLoaded)
                EditorSceneManager.CloseScene(mapScene, true);

            EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
        }

        GameMapDefinition definition = CreateInstance<GameMapDefinition>();
        definition.ConfigureEditor(
            mapId,
            mapName.Trim(),
            Path.GetFileNameWithoutExtension(scenePath),
            scenePath);

        AssetDatabase.CreateAsset(definition, definitionPath);

        if (!catalog.AddMapEditor(definition))
        {
            AssetDatabase.DeleteAsset(definitionPath);
            AssetDatabase.DeleteAsset(scenePath);
            EditorUtility.DisplayDialog(
                "Create Game Map",
                $"Map id {mapId} is already registered.",
                "OK");
            return;
        }

        EditorUtility.SetDirty(catalog);
        AddSceneToBuildSettings(scenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = definition;
        EditorGUIUtility.PingObject(definition);
    }

    private static void AddSceneToBuildSettings(string scenePath)
    {
        List<EditorBuildSettingsScene> scenes =
            new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

        for (int i = 0; i < scenes.Count; i++)
        {
            if (PathsEqual(scenes[i].path, scenePath))
                return;
        }

        scenes.Add(new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
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

    private static string SanitizeFileName(string value)
    {
        string sanitized = value.Trim();

        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            sanitized = sanitized.Replace(invalidCharacter, '_');

        return sanitized.Replace(' ', '_');
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
    private const string GameScenePath =
        "Assets/Collaborators/Qewzdl/Scenes/Game.unity";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        GameMapDefinition map = (GameMapDefinition)target;

        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(map.ScenePath)))
        {
            if (GUILayout.Button("Open Map Only"))
                OpenMapOnly(map);

            if (GUILayout.Button("Open With Game Shell"))
                OpenWithGameShell(map);
        }
    }

    private static void OpenMapOnly(GameMapDefinition map)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EditorSceneManager.OpenScene(map.ScenePath, OpenSceneMode.Single);
    }

    private static void OpenWithGameShell(GameMapDefinition map)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        Scene mapScene = EditorSceneManager.OpenScene(map.ScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(mapScene);
    }
}
