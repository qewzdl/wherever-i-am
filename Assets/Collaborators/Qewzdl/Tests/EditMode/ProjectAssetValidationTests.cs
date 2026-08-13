using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[Category("Baseline")]
public sealed class ProjectAssetValidationTests
{
    private const string ProjectSettingsPath =
        "Assets/Collaborators/Qewzdl/Settings/ProjectSettings.asset";
    private const string ProjectSceneFlowPath =
        "Assets/Collaborators/Qewzdl/Settings/ProjectSceneFlow.asset";
    private const string GameMapCatalogPath =
        "Assets/Collaborators/Qewzdl/Configs/Maps/GameMapCatalog.asset";
    private const string SceneAudioRegistryPath =
        "Assets/Collaborators/Qewzdl/Audio/Scenes/SceneAudioRegistry.asset";
    private const string UiSoundThemePath =
        "Assets/Collaborators/Qewzdl/Audio/SFX/UI/Themes/UiSoundTheme_Default.asset";
    private const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";
    private const string CollaboratorsRoot = "Assets/Collaborators";

    private static readonly ProjectSceneKind[] RequiredProjectScenes =
    {
        ProjectSceneKind.Bootstrap,
        ProjectSceneKind.MainMenu,
        ProjectSceneKind.Lobby,
        ProjectSceneKind.Game
    };

    private static readonly string[] SerializedAssetExtensions =
    {
        ".asset",
        ".controller",
        ".mat",
        ".overrideController",
        ".prefab",
        ".unity"
    };

    private static readonly Regex GuidReferencePattern = new(
        @"guid:\s*([0-9a-fA-F]{32})",
        RegexOptions.Compiled);

    [Test]
    public void ProjectScenes_AreUniqueResolvableAndEnabled()
    {
        ProjectSettings settings = LoadRequiredAsset<ProjectSettings>(ProjectSettingsPath);
        SerializedProperty scenes = new SerializedObject(settings).FindProperty("scenes");

        Assert.That(scenes, Is.Not.Null);
        Assert.That(scenes.arraySize, Is.GreaterThanOrEqualTo(RequiredProjectScenes.Length));
        Assert.That(settings.BootstrapScene, Is.EqualTo(ProjectSceneKind.Bootstrap));
        Assert.That(settings.DefaultStartupScene, Is.EqualTo(ProjectSceneKind.MainMenu));

        HashSet<ProjectSceneKind> kinds = new();
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> enabledBuildScenes = new(
            EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => NormalizePath(scene.path)),
            StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < scenes.arraySize; i++)
        {
            SerializedProperty definition = scenes.GetArrayElementAtIndex(i);
            ProjectSceneKind kind = (ProjectSceneKind)definition
                .FindPropertyRelative("kind")
                .intValue;
            string sceneName = definition.FindPropertyRelative("sceneName").stringValue;
            string scenePath = NormalizePath(
                definition.FindPropertyRelative("scenePath").stringValue);

            Assert.That(kind, Is.Not.EqualTo(ProjectSceneKind.Unknown));
            Assert.That(kinds.Add(kind), Is.True, $"Duplicate project scene kind: {kind}.");
            Assert.That(string.IsNullOrWhiteSpace(sceneName), Is.False, $"{kind} has no name.");
            Assert.That(names.Add(sceneName), Is.True, $"Duplicate project scene name: {sceneName}.");
            Assert.That(string.IsNullOrWhiteSpace(scenePath), Is.False, $"{kind} has no path.");
            Assert.That(paths.Add(scenePath), Is.True, $"Duplicate project scene path: {scenePath}.");
            Assert.That(
                AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath),
                Is.Not.Null,
                $"{kind} points to missing scene '{scenePath}'.");
            Assert.That(
                Path.GetFileNameWithoutExtension(scenePath),
                Is.EqualTo(sceneName),
                $"{kind} scene name does not match its asset filename.");

            if (kind != ProjectSceneKind.GameplayTest)
            {
                Assert.That(
                    enabledBuildScenes.Contains(scenePath),
                    Is.True,
                    $"{kind} scene '{scenePath}' is not enabled in build settings.");
            }
        }

        for (int i = 0; i < RequiredProjectScenes.Length; i++)
        {
            Assert.That(
                kinds.Contains(RequiredProjectScenes[i]),
                Is.True,
                $"Missing required project scene {RequiredProjectScenes[i]}.");
        }
    }

    [Test]
    public void ProjectSceneFlow_ContainsRequiredProductionTransitions()
    {
        ProjectSceneFlow flow = LoadRequiredAsset<ProjectSceneFlow>(ProjectSceneFlowPath);

        AssertTransition(flow, ProjectSceneKind.Bootstrap, ProjectSceneKind.MainMenu);
        AssertTransition(flow, ProjectSceneKind.MainMenu, ProjectSceneKind.Lobby);
        AssertTransition(flow, ProjectSceneKind.Lobby, ProjectSceneKind.Game);
        AssertTransition(flow, ProjectSceneKind.Lobby, ProjectSceneKind.MainMenu);
        AssertTransition(flow, ProjectSceneKind.Game, ProjectSceneKind.MainMenu);
    }

    [Test]
    public void SceneRuntimeAssets_MatchRequiredFeaturePolicies()
    {
        ProjectSettings settings = LoadRequiredAsset<ProjectSettings>(ProjectSettingsPath);

        for (int i = 1; i < RequiredProjectScenes.Length; i++)
        {
            ProjectSceneKind kind = RequiredProjectScenes[i];
            Assert.That(settings.TryGetScene(kind, out ProjectSceneDefinition definition), Is.True);

            ValidateSceneRuntime(definition, kind);
        }
    }

    [Test]
    public void GameMapCatalog_IsValidAndEveryMapSceneExists()
    {
        GameMapCatalog catalog = LoadRequiredAsset<GameMapCatalog>(GameMapCatalogPath);

        Assert.That(catalog.IsValid(out string catalogError), Is.True, catalogError);
        Assert.That(catalog.Count, Is.GreaterThan(0));
        Assert.That(catalog.IsValidMapId(catalog.DefaultMapId), Is.True);

        HashSet<string> enabledBuildScenes = new(
            EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => NormalizePath(scene.path)),
            StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < catalog.Count; i++)
        {
            GameMapDefinition map = catalog.GetMapAt(i);
            Assert.That(map, Is.Not.Null, $"Null map at catalog index {i}.");
            Assert.That(map.IsConfigured(out string mapError), Is.True, mapError);

            string scenePath = NormalizePath(map.ScenePath);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath),
                Is.Not.Null,
                $"Map {map.MapId} points to missing scene '{scenePath}'.");
            Assert.That(
                enabledBuildScenes.Contains(scenePath),
                Is.True,
                $"Map scene '{scenePath}' is not enabled in build settings.");
        }
    }

    [Test]
    public void ObjectiveAndEnemyConfigs_AreComplete()
    {
        string[] sequenceGuids = AssetDatabase.FindAssets(
            $"t:{nameof(ObjectiveSequenceDefinition)}",
            new[] { CollaboratorsRoot });
        string[] enemyConfigGuids = AssetDatabase.FindAssets(
            $"t:{nameof(EnemyConfig)}",
            new[] { CollaboratorsRoot });

        Assert.That(sequenceGuids.Length, Is.GreaterThan(0));
        Assert.That(enemyConfigGuids.Length, Is.GreaterThan(0));

        for (int i = 0; i < sequenceGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(sequenceGuids[i]);
            ObjectiveSequenceDefinition sequence =
                AssetDatabase.LoadAssetAtPath<ObjectiveSequenceDefinition>(path);

            Assert.That(sequence, Is.Not.Null, path);
            Assert.That(sequence.IsValid(out string error), Is.True, $"{path}: {error}");
        }

        // A map may swap in its own objective sequence. Nothing loads it until
        // the match is already starting, so a broken override only shows up as
        // a faulted flow mid-session unless it is checked here.
        string[] mapGuids = AssetDatabase.FindAssets(
            $"t:{nameof(GameMapDefinition)}",
            new[] { CollaboratorsRoot });

        for (int i = 0; i < mapGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(mapGuids[i]);
            GameMapDefinition map =
                AssetDatabase.LoadAssetAtPath<GameMapDefinition>(path);

            Assert.That(map, Is.Not.Null, path);

            if (map.ObjectiveSequenceOverride == null)
                continue;

            Assert.That(
                map.ObjectiveSequenceOverride.IsValid(out string overrideError),
                Is.True,
                $"{path}: {overrideError}");
        }

        for (int i = 0; i < enemyConfigGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(enemyConfigGuids[i]);
            EnemyConfig config = AssetDatabase.LoadAssetAtPath<EnemyConfig>(path);

            Assert.That(config, Is.Not.Null, path);
            Assert.That(config.TryGetValidationError(out string error), Is.False, $"{path}: {error}");
        }
    }

    // Stalk, Retreat, Flank and Ambush shipped with no presentation entries at
    // all, so four of the nine states were silent and left the animator on
    // whatever the previous state set. Nothing failed - the profile simply had
    // no row - which is exactly the kind of gap a lookup by state hides.
    [Test]
    public void EnemyPresentationProfiles_CoverEveryEnemyState()
    {
        string[] profileGuids = AssetDatabase.FindAssets(
            $"t:{nameof(EnemyPresentationProfile)}",
            new[] { CollaboratorsRoot });

        Assert.That(profileGuids.Length, Is.GreaterThan(0));

        foreach (EnemyState state in Enum.GetValues(typeof(EnemyState)))
        {
            for (int i = 0; i < profileGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(profileGuids[i]);
                EnemyPresentationProfile profile =
                    AssetDatabase.LoadAssetAtPath<EnemyPresentationProfile>(path);

                Assert.That(profile, Is.Not.Null, path);
                Assert.That(
                    profile.TryGetPresentation(state, out _),
                    Is.True,
                    $"{path} has no presentation entry for {state}.");
            }
        }
    }

    [Test]
    public void AudioAssets_HavePlayableTracksEffectsAndCompleteUiTheme()
    {
        string[] trackGuids = AssetDatabase.FindAssets(
            $"t:{nameof(MusicTrack)}",
            new[] { CollaboratorsRoot });
        string[] effectGuids = AssetDatabase.FindAssets(
            $"t:{nameof(SoundEffect)}",
            new[] { CollaboratorsRoot });

        Assert.That(trackGuids.Length, Is.GreaterThan(0));
        Assert.That(effectGuids.Length, Is.GreaterThan(0));

        HashSet<string> trackIds = new(StringComparer.Ordinal);

        for (int i = 0; i < trackGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(trackGuids[i]);
            MusicTrack track = AssetDatabase.LoadAssetAtPath<MusicTrack>(path);

            Assert.That(track, Is.Not.Null, path);
            Assert.That(string.IsNullOrWhiteSpace(track.TrackId), Is.False, path);
            Assert.That(trackIds.Add(track.TrackId), Is.True, $"Duplicate track id '{track.TrackId}'.");
            Assert.That(track.Clip, Is.Not.Null, $"{path} has no AudioClip.");
        }

        for (int i = 0; i < effectGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(effectGuids[i]);
            SoundEffect effect = AssetDatabase.LoadAssetAtPath<SoundEffect>(path);
            SerializedProperty clips = new SerializedObject(effect).FindProperty("clips");

            Assert.That(effect, Is.Not.Null, path);
            Assert.That(clips, Is.Not.Null, path);
            Assert.That(clips.arraySize, Is.GreaterThan(0), $"{path} has no clips.");

            for (int clipIndex = 0; clipIndex < clips.arraySize; clipIndex++)
            {
                Assert.That(
                    clips.GetArrayElementAtIndex(clipIndex).objectReferenceValue,
                    Is.Not.Null,
                    $"{path} has a null clip at index {clipIndex}.");
            }
        }

        UiSoundTheme theme = LoadRequiredAsset<UiSoundTheme>(UiSoundThemePath);

        foreach (UiSoundType soundType in Enum.GetValues(typeof(UiSoundType)))
        {
            Assert.That(
                theme.TryGetSound(soundType, out SoundEffect sound),
                Is.True,
                $"UI theme is missing {soundType}.");
            Assert.That(sound, Is.Not.Null, $"UI theme resolved null for {soundType}.");
        }

        SceneAudioRegistry registry =
            LoadRequiredAsset<SceneAudioRegistry>(SceneAudioRegistryPath);
        Assert.That(registry.GetProfileForScene("Main Menu"), Is.Not.Null);
        Assert.That(registry.GetProfileForScene("Game"), Is.Not.Null);
    }

    [Test]
    public void NetworkPrefabCatalog_HasValidUniqueNetworkObjectsAndPlayerPrefab()
    {
        NetworkPrefabsList prefabs =
            LoadRequiredAsset<NetworkPrefabsList>(NetworkPrefabsPath);

        Assert.That(prefabs.PrefabList.Count, Is.GreaterThan(0));

        HashSet<uint> hashes = new();
        bool hasPlayerPrefab = false;
        GameObject playerPrefab = LoadBootstrapPlayerPrefab();

        for (int i = 0; i < prefabs.PrefabList.Count; i++)
        {
            NetworkPrefab entry = prefabs.PrefabList[i];

            Assert.That(entry, Is.Not.Null, $"Null network prefab entry at index {i}.");
            Assert.That(entry.Validate(i), Is.True, $"Invalid network prefab at index {i}.");

            uint hash = entry.SourcePrefabGlobalObjectIdHash;
            Assert.That(hash, Is.Not.EqualTo(0), $"Network prefab index {i} has zero hash.");
            Assert.That(hashes.Add(hash), Is.True, $"Duplicate network prefab hash {hash}.");

            if (entry.Prefab == playerPrefab)
                hasPlayerPrefab = true;
        }

        Assert.That(playerPrefab, Is.Not.Null, "Bootstrap NetworkManager has no player prefab.");
        Assert.That(
            playerPrefab.TryGetComponent(out NetworkObject _),
            Is.True,
            "Player prefab has no NetworkObject.");
        Assert.That(hasPlayerPrefab, Is.True, "Player prefab is absent from NetworkPrefabsList.");
    }

    [Test]
    public void GameplayCameras_HandOffFromSceneCameraToLocalPlayer()
    {
        GameObject playerPrefab = LoadBootstrapPlayerPrefab();
        Assert.That(playerPrefab, Is.Not.Null);

        Camera[] playerCameras = playerPrefab.GetComponentsInChildren<Camera>(true);
        Assert.That(playerCameras, Is.Not.Empty, "Player prefab has no camera.");
        Assert.That(
            playerCameras.All(camera => !camera.enabled),
            Is.True,
            "Player prefab cameras must start disabled so a remote player never renders.");

        float lowestPlayerCameraDepth = playerCameras.Min(camera => camera.depth);

        AudioListener playerListener =
            playerPrefab.GetComponentInChildren<AudioListener>(true);
        Assert.That(playerListener, Is.Not.Null, "Player prefab has no audio listener.");
        Assert.That(
            playerListener.enabled,
            Is.False,
            "Player prefab audio listener must start disabled so a remote player never hears.");

        ProjectSettings settings = LoadRequiredAsset<ProjectSettings>(ProjectSettingsPath);
        Assert.That(
            settings.TryGetScene(ProjectSceneKind.Game, out ProjectSceneDefinition game),
            Is.True);

        Scene existingScene = SceneManager.GetSceneByPath(game.ScenePath);
        bool openedByTest = !existingScene.IsValid() || !existingScene.isLoaded;
        Scene scene = openedByTest
            ? EditorSceneManager.OpenScene(game.ScenePath, OpenSceneMode.Additive)
            : existingScene;

        try
        {
            List<Camera> sceneCameras = new();
            GameObject[] roots = scene.GetRootGameObjects();

            for (int i = 0; i < roots.Length; i++)
            {
                sceneCameras.AddRange(roots[i].GetComponentsInChildren<Camera>(true));
            }

            for (int i = 0; i < sceneCameras.Count; i++)
            {
                Camera sceneCamera = sceneCameras[i];

                Assert.That(
                    sceneCamera.GetComponent<FallbackCamera>(),
                    Is.Not.Null,
                    $"'{GetHierarchyPath(sceneCamera.transform)}' keeps rendering over the " +
                    $"local player because nothing hands the view over.");

                Assert.That(
                    sceneCamera.depth,
                    Is.LessThan(lowestPlayerCameraDepth),
                    $"'{GetHierarchyPath(sceneCamera.transform)}' shares its depth with a " +
                    $"player camera, so the winner of the last draw is undefined.");
            }
        }
        finally
        {
            if (openedByTest && scene.IsValid())
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void CollaboratorPrefabs_HaveNoMissingScripts()
    {
        string[] prefabGuids = AssetDatabase.FindAssets(
            "t:Prefab",
            new[] { CollaboratorsRoot });
        List<string> failures = new();

        for (int i = 0; i < prefabGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null)
            {
                failures.Add($"{path}: could not load prefab.");
                continue;
            }

            Transform[] transforms = prefab.GetComponentsInChildren<Transform>(true);

            for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
            {
                GameObject current = transforms[transformIndex].gameObject;
                int missingCount =
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(current);

                if (missingCount > 0)
                {
                    failures.Add(
                        $"{path}: '{GetHierarchyPath(current.transform)}' has " +
                        $"{missingCount} missing script(s).");
                }
            }
        }

        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
    }

    [Test]
    public void CollaboratorSerializedAssets_HaveNoBrokenGuidReferences()
    {
        string root = Path.GetFullPath(CollaboratorsRoot);
        string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        List<string> failures = new();

        for (int i = 0; i < files.Length; i++)
        {
            string extension = Path.GetExtension(files[i]);

            if (!SerializedAssetExtensions.Contains(
                    extension,
                    StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            string contents = File.ReadAllText(files[i]);
            MatchCollection references = GuidReferencePattern.Matches(contents);

            for (int referenceIndex = 0;
                 referenceIndex < references.Count;
                 referenceIndex++)
            {
                string guid = references[referenceIndex].Groups[1].Value;

                if (IsBuiltInGuid(guid) ||
                    !string.IsNullOrWhiteSpace(AssetDatabase.GUIDToAssetPath(guid)))
                {
                    continue;
                }

                failures.Add(
                    $"{ToAssetPath(files[i])}: missing asset for guid {guid}.");
            }
        }

        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures.Distinct()));
    }

    private static T LoadRequiredAsset<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Assert.That(asset, Is.Not.Null, $"Required asset is missing: {path}");
        return asset;
    }

    private static void AssertTransition(
        ProjectSceneFlow flow,
        ProjectSceneKind from,
        ProjectSceneKind to)
    {
        Assert.That(
            flow.TryGetTransition(from, to, out _),
            Is.True,
            $"Missing scene transition {from} -> {to}.");
    }

    private static void ValidateSceneRuntime(
        ProjectSceneDefinition definition,
        ProjectSceneKind expectedKind)
    {
        Scene existingScene = SceneManager.GetSceneByPath(definition.ScenePath);
        bool openedByTest = !existingScene.IsValid() || !existingScene.isLoaded;
        Scene scene = openedByTest
            ? EditorSceneManager.OpenScene(definition.ScenePath, OpenSceneMode.Additive)
            : existingScene;

        try
        {
            List<SceneRuntime> runtimes = new();
            GameObject[] roots = scene.GetRootGameObjects();

            for (int i = 0; i < roots.Length; i++)
            {
                runtimes.AddRange(roots[i].GetComponentsInChildren<SceneRuntime>(true));
            }

            Assert.That(
                runtimes.Count,
                Is.EqualTo(1),
                $"{definition.ScenePath} must contain exactly one SceneRuntime.");

            SceneRuntime runtime = runtimes[0];
            Assert.That(runtime.SceneKind, Is.EqualTo(expectedKind));
            Assert.That(runtime.Features, Is.Not.Null);
            Assert.That(runtime.Features.Length, Is.GreaterThan(0));
            Assert.That(runtime.Features.All(feature => feature != null), Is.True);
            Assert.That(
                runtime.Features.Distinct().Count(),
                Is.EqualTo(runtime.Features.Length),
                $"{definition.ScenePath} contains duplicate feature references.");

            Assert.That(
                ProjectSceneScopePolicy.TryGetRequirements(
                    expectedKind,
                    false,
                    out ProjectSceneScopeRequirements requirements),
                Is.True);
            Assert.That(
                requirements.ValidateConfiguredFeatures(
                    runtime.Features,
                    definition.ScenePath),
                Is.True);
        }
        finally
        {
            if (openedByTest && scene.IsValid())
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static GameObject LoadBootstrapPlayerPrefab()
    {
        ProjectSettings settings = LoadRequiredAsset<ProjectSettings>(ProjectSettingsPath);
        Assert.That(
            settings.TryGetScene(ProjectSceneKind.Bootstrap, out ProjectSceneDefinition bootstrap),
            Is.True);

        Scene existingScene = SceneManager.GetSceneByPath(bootstrap.ScenePath);
        bool openedByTest = !existingScene.IsValid() || !existingScene.isLoaded;
        Scene scene = openedByTest
            ? EditorSceneManager.OpenScene(bootstrap.ScenePath, OpenSceneMode.Additive)
            : existingScene;

        try
        {
            List<NetworkManager> managers = new();
            GameObject[] roots = scene.GetRootGameObjects();

            for (int i = 0; i < roots.Length; i++)
            {
                managers.AddRange(roots[i].GetComponentsInChildren<NetworkManager>(true));
            }

            Assert.That(managers.Count, Is.EqualTo(1));
            return managers[0].NetworkConfig.PlayerPrefab;
        }
        finally
        {
            if (openedByTest && scene.IsValid())
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/');
    }

    private static string GetHierarchyPath(Transform transform)
    {
        List<string> names = new();

        while (transform != null)
        {
            names.Add(transform.name);
            transform = transform.parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }

    private static bool IsBuiltInGuid(string guid)
    {
        return string.IsNullOrWhiteSpace(guid) ||
               guid.All(character => character == '0');
    }

    private static string ToAssetPath(string fullPath)
    {
        string normalizedFullPath = NormalizePath(Path.GetFullPath(fullPath));
        string normalizedProjectPath = NormalizePath(Path.GetFullPath("."));

        return normalizedFullPath.StartsWith(
            normalizedProjectPath,
            StringComparison.OrdinalIgnoreCase)
            ? normalizedFullPath.Substring(normalizedProjectPath.Length + 1)
            : normalizedFullPath;
    }
}
