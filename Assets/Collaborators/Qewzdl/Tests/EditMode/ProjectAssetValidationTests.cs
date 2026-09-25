using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
    private const string LobbyConfigPath =
        "Assets/Collaborators/Qewzdl/Configs/Lobby/LobbyConfig.asset";
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

    // Connection approval refuses to configure itself without a build version,
    // and without approval there is no networking at all. An empty version
    // field is therefore not a release-day detail - it takes the whole session
    // down, and it does it at runtime where the cause is far from the symptom.
    [Test]
    public void BuildVersion_IsSetAndFitsTheConnectionPayload()
    {
        Assert.That(
            Application.version,
            Is.Not.Null.And.Not.Empty,
            "Player Settings has no version. Connection approval cannot start without one.");
        Assert.That(
            Application.version.Trim(),
            Is.Not.Empty,
            "Player Settings version is whitespace. Connection approval treats that as missing.");
        Assert.That(
            Encoding.UTF8.GetByteCount(Application.version.Trim()),
            Is.LessThanOrEqualTo(64),
            "The build version does not fit the connection approval payload.");
    }

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

    // One route per enemy, enforced where every spawn point on a map can be
    // seen at once.
    //
    // Nothing in the code stops two spawn points pointing at the same
    // EnemyPatrolRoute, and nothing goes wrong in an obvious way if they do:
    // both enemies walk the same loop, each starting from whichever point is
    // nearest, and they bunch up or trail each other depending on where they
    // spawned. It looks like a pathing bug and it is a wiring one.
    //
    // A rule rather than a runtime refusal on purpose. Refusing to hand out a
    // route twice would leave the second enemy standing still for the match,
    // which is a worse thing to ship than two enemies sharing a circuit. This
    // fails before anybody plays instead.
    //
    // If two enemies patrolling one circuit is ever wanted - a pair of guards
    // on the same corridor is a real design - this assertion is the thing to
    // relax, and it should be relaxed deliberately rather than by somebody
    // dragging the same object into two slots.
    [Test]
    public void MapSpawnPoints_EachPatrolADifferentRoute()
    {
        GameMapCatalog catalog =
            LoadRequiredAsset<GameMapCatalog>(GameMapCatalogPath);

        List<string> problems = new();

        // Counted as well as checked. With one spawn point on one map this
        // assertion is vacuous, and a version of it that found no spawn points
        // at all - a renamed component, a map whose scene stopped opening, a
        // catalog entry with an empty path - would be vacuous for ever and look
        // exactly the same from the outside. The count is what says the test is
        // still looking at something.
        int routedSpawnPoints = 0;

        for (int i = 0; i < catalog.Count; i++)
        {
            GameMapDefinition map = catalog.GetMapAt(i);

            if (map == null || string.IsNullOrWhiteSpace(map.ScenePath))
            {
                continue;
            }

            Scene existingScene = SceneManager.GetSceneByPath(map.ScenePath);
            bool openedByTest = !existingScene.IsValid() || !existingScene.isLoaded;
            Scene scene = openedByTest
                ? EditorSceneManager.OpenScene(map.ScenePath, OpenSceneMode.Additive)
                : existingScene;

            try
            {
                List<EnemySpawnPoint> spawnPoints = new();

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    spawnPoints.AddRange(
                        root.GetComponentsInChildren<EnemySpawnPoint>(true));
                }

                Dictionary<EnemyPatrolRoute, int> routeUses = new();

                foreach (EnemySpawnPoint spawnPoint in spawnPoints)
                {
                    EnemyPatrolRoute route = spawnPoint.PatrolRoute;

                    // An enemy with no route stands where it was put until
                    // something happens, which is a legitimate way to place a
                    // sentry. Sharing is the mistake, not going without.
                    if (route == null)
                    {
                        continue;
                    }

                    routedSpawnPoints++;
                    routeUses.TryGetValue(route, out int uses);
                    routeUses[route] = uses + 1;
                }

                foreach (KeyValuePair<EnemyPatrolRoute, int> entry in routeUses)
                {
                    if (entry.Value > 1)
                    {
                        problems.Add(
                            $"{map.ScenePath}: {entry.Value} spawn points share " +
                            $"the patrol route '{entry.Key.name}'");
                    }
                }
            }
            finally
            {
                if (openedByTest && scene.IsValid())
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        Assert.That(problems, Is.Empty, string.Join("; ", problems));

        Assert.That(
            routedSpawnPoints,
            Is.GreaterThan(0),
            "No enemy spawn point on any map has a patrol route, so this test " +
            "is checking nothing and would go on passing however the routes " +
            "were wired.");
    }

    // The promise that makes the approach speed worth having: moving quietly
    // never makes an enemy run at you, on the difficulty most people play.
    //
    // A heard noise's strength at her is its loudness scaled down by distance,
    // so the most it can ever be is its own loudness, heard from where it was
    // made. Checked at that worst case, against the shipped presets and every
    // enemy's own threshold, because the failure is silent from both ends:
    // nudge the walking step up a little, or a threshold down a little, and
    // quiet play quietly stops being rewarded with nothing to say so.
    [Test]
    public void QuietMovement_NeverMakesAnEnemyRun_OnTheDefaultDifficulty()
    {
        GameDifficultyCatalog game = EnemyFactory.GameDifficulties;
        string[] quietPresets = { "Noise_FootstepWalking", "Noise_Breath" };
        List<string> problems = new();

        foreach (NetworkEnemyController enemy in Enemies())
        {
            EnemyConfig played = NetworkEnemyController.ChooseDifficultyConfig(
                enemy.DifficultyCatalog,
                game.DefaultDifficultyId,
                enemy.Config);

            if (played == null || played.InvestigationProfile == null)
            {
                continue;
            }

            foreach (string presetName in quietPresets)
            {
                string[] guids = AssetDatabase.FindAssets(
                    $"{presetName} t:{nameof(GameplayNoisePreset)}");

                Assert.That(guids, Is.Not.Empty, $"No preset called {presetName}.");

                GameplayNoisePreset preset =
                    AssetDatabase.LoadAssetAtPath<GameplayNoisePreset>(
                        AssetDatabase.GUIDToAssetPath(guids[0]));

                if (preset.Loudness >= played.urgentNoiseScore)
                {
                    problems.Add(
                        $"{enemy.name}: {presetName} heard from where it was made reaches " +
                        $"{preset.Loudness:F2}, at or over the {played.urgentNoiseScore:F2} " +
                        "at which it runs - so moving quietly can make it run at you");
                }
            }
        }

        Assert.That(problems, Is.Empty, string.Join("; ", problems));
    }

    // Every kind of enemy has to be able to play every difficulty the game
    // offers, from its own catalog.
    //
    // An enemy whose catalog is missing a difficulty does not fail when that
    // difficulty is picked. It falls back to its prefab's config - so on that
    // difficulty it quietly plays another one, and nothing anywhere says so.
    // The catalog's own validity is asked too: it is what keeps every
    // difficulty of one enemy describing the same body, which the clients
    // build their collider from.
    [Test]
    public void EveryEnemy_PlaysEveryDifficultyTheGameOffers()
    {
        GameDifficultyCatalog game = EnemyFactory.GameDifficulties;
        List<string> problems = new();

        foreach (NetworkEnemyController enemy in Enemies())
        {
            EnemyDifficultyCatalog own = enemy.DifficultyCatalog;

            if (own == null)
            {
                problems.Add($"{enemy.name} has no difficulty catalog of its own");
                continue;
            }

            if (!own.IsValid(out string error))
            {
                problems.Add($"{enemy.name}: {error}");
            }

            if (!own.CoversEvery(game, out string missing))
            {
                problems.Add($"{enemy.name} has no config for {missing}");
            }
        }

        Assert.That(problems, Is.Empty, string.Join("; ", problems));
    }

    // Whatever is in the network prefab list goes into the build, and Netcode
    // adds every NetworkObject prefab it sees - the tests' own enemy included.
    [Test]
    public void NetworkPrefabList_ShipsNothingFromTheTests()
    {
        NetworkPrefabsList networkPrefabs =
            LoadRequiredAsset<NetworkPrefabsList>(EnemyFactory.NetworkPrefabsPath);

        string[] tests = networkPrefabs.PrefabList
            .Where(entry => entry?.Prefab != null)
            .Select(entry => AssetDatabase.GetAssetPath(entry.Prefab))
            .Where(TestPrefabsOutOfNetworkList.IsTestPath)
            .ToArray();

        Assert.That(tests, Is.Empty,
            "These test prefabs would ship with the game: " + string.Join(", ", tests));
    }

    // No enemy is load-bearing. The one thing allowed to depend on an enemy is
    // a map that places it; anything else that refers to its files - the base
    // prefab, the lobby, another enemy, a shared gizmo - would mean deleting
    // that enemy breaks something that is not about it. That is how the first
    // enemy ended up holding the game's difficulties, and it was found by
    // trying to delete her.
    //
    // Asked through the same plan the setup window's Delete uses, so what
    // this passes is exactly what the window would let go.
    [Test]
    public void EveryEnemy_CouldBeDeleted_ButForTheMapsThatPlaceIt()
    {
        List<string> problems = new();

        foreach (NetworkEnemyController enemy in Enemies())
        {
            EnemyFactory.DeletionPlan plan = EnemyFactory.PlanDeletion(
                enemy,
                EnemyFactory.ConfigRoot,
                EnemyFactory.NetworkPrefabsPath);

            problems.AddRange(plan.Blockers
                .Where(reason => !reason.StartsWith(EnemyFactory.MapsFolder + "/", StringComparison.Ordinal))
                .Select(reason => $"{enemy.name}: {reason}"));
        }

        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    // One folder layout for every enemy, so any two enemies can be read the
    // same way. The setup window offers to tidy a folder that has drifted.
    [Test]
    public void EveryEnemy_KeepsItsFilesInTheOneFolderLayout()
    {
        List<string> problems = new();

        foreach (NetworkEnemyController enemy in Enemies())
        {
            problems.AddRange(EnemyFactory.PlanTidy(enemy, EnemyFactory.ConfigRoot)
                .Select(move => $"{move.From} belongs at {move.To}"));
        }

        Assert.That(problems, Is.Empty,
            "Open Enemy Setup and tidy these enemies' folders: " + string.Join("; ", problems));
    }

    // Every enemy the game can spawn. The project may have none - a legitimate
    // state between deleting one enemy and making the next - so the checks
    // that walk these are about each enemy, not about there being one.
    private static List<NetworkEnemyController> Enemies()
    {
        return EnemyFactory.FindEnemies(
            LoadRequiredAsset<NetworkPrefabsList>(EnemyFactory.NetworkPrefabsPath));
    }

    // An enemy's configs, one per difficulty it has, easiest first by id and
    // named after the game's difficulty.
    private static List<(string Name, EnemyConfig Config)> DifficultyConfigsOf(
        NetworkEnemyController enemy)
    {
        GameDifficultyCatalog game = EnemyFactory.GameDifficulties;
        List<(int Id, string Name, EnemyConfig Config)> configs = new();
        EnemyDifficultyCatalog own = enemy.DifficultyCatalog;

        for (int i = 0; own != null && i < own.Count; i++)
        {
            if (own.TryGetEntryAt(i, out EnemyDifficultyCatalog.EnemyDifficultyEntry entry) &&
                entry.Config != null)
            {
                string name = game.TryGet(entry.DifficultyId, out GameDifficultyCatalog.Difficulty difficulty)
                    ? difficulty.DisplayName
                    : $"difficulty {entry.DifficultyId}";

                configs.Add((entry.DifficultyId, $"{enemy.name} {name}", entry.Config));
            }
        }

        return configs
            .OrderBy(config => config.Id)
            .Select(config => (config.Name, config.Config))
            .ToList();
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
    public void GameDifficultyCatalog_IsValidAndEveryDifficultyIsSelectable()
    {
        GameDifficultyCatalog catalog = EnemyFactory.GameDifficulties;

        Assert.That(catalog, Is.Not.Null, $"No game difficulty catalog at {EnemyFactory.GameDifficultiesPath}.");
        Assert.That(catalog.IsValid(out string catalogError), Is.True, catalogError);

        LobbyConfig lobbyConfig = LoadRequiredAsset<LobbyConfig>(LobbyConfigPath);

        Assert.That(
            lobbyConfig.DifficultyCatalog,
            Is.SameAs(catalog),
            "Lobby offers difficulties from a different catalog than the one under test.");

        for (int i = 0; i < catalog.Count; i++)
        {
            Assert.That(
                catalog.TryGetAt(i, out GameDifficultyCatalog.Difficulty difficulty),
                Is.True,
                $"Missing difficulty at index {i}.");

            // A difficulty the lobby refuses is one nobody can ever pick.
            Assert.That(
                lobbyConfig.IsValidDifficultyId(difficulty.DifficultyId),
                Is.True,
                $"Difficulty '{difficulty.DisplayName}' is not selectable in the lobby.");
        }
    }

    // Fields that are the same on every difficulty on purpose. A profile
    // authored once per difficulty is a claim that its numbers are a lever;
    // anything in it that never moves is either one of these or was forgotten
    // in a copy, and the difference between those two is a decision worth
    // writing down.
    private static readonly HashSet<string> SharedDifficultyFields = new(StringComparer.Ordinal)
    {
        // Anatomy and wiring, not difficulty.
        "EnemyVisionConfig.targetHeightOffset",

        // A floor for inaudible sources. hearingSensitivity is the lever.
        "EnemyHearingConfig.minimumNoiseLoudness",

        // How close counts as having reached a search point.
        "EnemyInvestigationConfig.investigationReachDistance",

        // Retreat and flank geometry: the shape of a manoeuvre, not how hard
        // she is. What varies is when she chooses one - see the stalk and
        // ambush fields, which are not on this list.
        "EnemyStealthTacticsConfig.noEscapeTimeout",
        "EnemyStealthTacticsConfig.failedRouteAvoidRadius",
        "EnemyStealthTacticsConfig.retreatBrokenSightDuration",
        "EnemyStealthTacticsConfig.retreatTimeout",
        "EnemyStealthTacticsConfig.retreatDistance",
        "EnemyStealthTacticsConfig.retreatRepathInterval",
        "EnemyStealthTacticsConfig.flankBehindDistance",

        // Planner budget. Raising it for one difficulty buys frame time, not
        // difficulty.
        "EnemyStealthTacticsConfig.claimSpacing",
        "EnemyStealthTacticsConfig.arrivalDistance",
        "EnemyStealthTacticsConfig.routeSampleSpacing",
        "EnemyStealthTacticsConfig.routeSampleBudget",
        "EnemyStealthTacticsConfig.candidatesPerTick",
    };

    // acceleration and angularSpeed sat at 12 and 360 on all four difficulties
    // while only top speed varied, and in a house that is most of what a chase
    // is. Nothing said so until it was played. This says so.
    [Test]
    public void EnemyDifficultyProfiles_HaveNoFieldThatNeverMoves()
    {
        List<string> deadFields = new();

        foreach (NetworkEnemyController enemy in Enemies())
        {
            deadFields.AddRange(
                DeadFieldsOf(DifficultyConfigsOf(enemy).Select(config => config.Config).ToList())
                    .Select(field => $"{enemy.name}: {field}"));
        }

        Assert.That(
            deadFields,
            Is.Empty,
            "These look tuned per difficulty but never change, so they are not levers. " +
            "Give them different values or add them to " +
            $"{nameof(SharedDifficultyFields)} with a reason:\n  " +
            string.Join("\n  ", deadFields));
    }

    private static List<string> DeadFieldsOf(List<EnemyConfig> configs)
    {
        List<string> deadFields = new();

        if (configs.Count < 2)
        {
            return deadFields;
        }

        foreach (FieldInfo profileField in ProfileFields())
        {
            ScriptableObject[] profiles = configs
                .Select(config => (ScriptableObject)profileField.GetValue(config))
                .ToArray();

            // Every difficulty pointing at one asset is a single deliberate
            // decision to share, not a field-by-field oversight.
            if (profiles.Any(profile => profile == null) ||
                profiles.Distinct().Count() == 1)
            {
                continue;
            }

            foreach (FieldInfo field in profiles[0].GetType()
                         .GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                // Arrays hold the shape of a manoeuvre and compare by
                // reference here, so they are left out rather than wrongly
                // reported as differing.
                if (field.FieldType.IsArray)
                {
                    continue;
                }

                string id = $"{field.DeclaringType?.Name}.{field.Name}";

                if (SharedDifficultyFields.Contains(id))
                {
                    continue;
                }

                object first = field.GetValue(profiles[0]);

                if (profiles.Skip(1).All(profile => Equals(field.GetValue(profile), first)))
                {
                    deadFields.Add($"{id} = {first} on every difficulty");
                }
            }
        }

        return deadFields;
    }

    private static IEnumerable<FieldInfo> ProfileFields()
    {
        return typeof(EnemyConfig)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(field =>
                field.IsDefined(typeof(SerializeField), inherit: false) &&
                typeof(ScriptableObject).IsAssignableFrom(field.FieldType));
    }

    // The bug this guards: hearing radius is capped by how far each noise
    // carries, so raising it past the loudest noise in the project changed
    // nothing and three of the four difficulties heard identically.
    //
    // Per enemy. An enemy whose difficulties all share one hearing profile has
    // decided hearing is not its lever, and is only held to never hearing
    // less as the difficulty rises.
    [Test]
    public void EnemyDifficulties_HearNoLessAsTheyGetHarder()
    {
        float[] noiseRadii = AssetDatabase.FindAssets("t:GameplayNoisePreset")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<GameplayNoisePreset>)
            .Where(preset => preset != null)
            .Select(preset => preset.Radius)
            .ToArray();

        Assert.That(noiseRadii, Is.Not.Empty, "No noise presets to measure hearing against.");

        List<string> problems = new();

        foreach (NetworkEnemyController enemy in Enemies())
        {
            List<(string Name, EnemyConfig Config)> configs = DifficultyConfigsOf(enemy)
                .Where(config => config.Config.HearingProfile != null)
                .ToList();

            if (configs.Count < 2)
            {
                continue;
            }

            bool anyDifference = false;

            foreach (float noiseRadius in noiseRadii)
            {
                for (int i = 1; i < configs.Count; i++)
                {
                    float previous = EffectiveHearing(configs[i - 1].Config, noiseRadius);
                    float current = EffectiveHearing(configs[i].Config, noiseRadius);

                    if (current < previous && !Mathf.Approximately(current, previous))
                    {
                        problems.Add(
                            $"'{configs[i].Name}' hears a noise of radius {noiseRadius} from " +
                            $"{current}, less than '{configs[i - 1].Name}' at {previous}");
                    }

                    anyDifference |= !Mathf.Approximately(current, previous);
                }
            }

            bool sharesOneHearingProfile =
                configs.Select(config => config.Config.HearingProfile).Distinct().Count() == 1;

            if (!anyDifference && !sharesOneHearingProfile)
            {
                problems.Add(
                    $"{enemy.name} has a hearing profile per difficulty, but every difficulty " +
                    "hears every noise from the same distance, so hearing is not a lever at all");
            }
        }

        Assert.That(problems, Is.Empty, string.Join("; ", problems));
    }

    private static float EffectiveHearing(EnemyConfig config, float noiseRadius)
    {
        return GameplayNoiseWorldService.ResolveEffectiveRadius(
            config.hearingRadius,
            config.hearingSensitivity,
            noiseRadius);
    }

    // Asked of the AssetDatabase rather than of the file system, because the
    // two disagreeing is exactly the failure this is for: an asset written by
    // hand, correct on disk, and never imported - so it exists everywhere
    // except in the editor, which is the only place anybody looks.
    //
    // This is also the whole safety net now. The brain used to substitute the
    // full set for an empty list, which meant a config nobody had filled in
    // still produced a working enemy and nothing ever said so. It no longer
    // does: an empty list is an enemy that stands still for the match. Catching
    // that here, before anybody plays, is the trade that made removing the
    // substitution worth doing.
    [Test]
    public void EnemyConfigs_ListBehaviorsThatAddUpToAnEnemy()
    {
        string[] configGuids = AssetDatabase.FindAssets(
            $"t:{nameof(EnemyConfig)}");

        Assert.That(configGuids, Is.Not.Empty);

        List<string> problems = new();

        foreach (string guid in configGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EnemyConfig config =
                AssetDatabase.LoadAssetAtPath<EnemyConfig>(path);

            if (config == null)
            {
                continue;
            }

            // The rules live in EnemyBehaviorListRules, shared with the enemy
            // setup window, so the build and the window cannot disagree about
            // what a sensible list is.
            foreach (string problem in EnemyBehaviorListRules.ProblemsWith(config))
            {
                problems.Add($"{path} {problem}");
            }
        }

        Assert.That(problems, Is.Empty, string.Join("; ", problems));
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
            Assert.That(
                entry.Validate(i),
                Is.True,
                $"Invalid network prefab at index {i}." +
                (entry.Prefab == null
                    ? " Its prefab no longer exists; Enemy Setup offers to remove " +
                      "the entry."
                    : string.Empty));

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

    // The setup window looks its fields up by name, and a rename it did not
    // follow turns a warning into a silence.
    //
    // It does say so at runtime now - a name matching nothing draws a red box -
    // but only to somebody who opens the window, and the whole point of that
    // block is to be read by somebody who has forgotten to. Here it is read by
    // the suite instead.
    [Test]
    public void PlayerSetupWindow_LooksUpFieldsThatStillExist()
    {
        AssertSerializedField<FootstepEmitter>("movement");
        AssertSerializedField<FootstepEmitter>("noiseEmitter");
        AssertSerializedField<FootstepEmitter>("runningPreset");
        AssertSerializedField<FootstepEmitter>("walkingPreset");
        AssertSerializedField<FootstepEmitter>("runningSound");
        AssertSerializedField<FootstepEmitter>("walkingSound");
        AssertSerializedField<PlayerBreathingSounds>("inhale");
        AssertSerializedField<PlayerBreathingSounds>("exhale");
        AssertSerializedField<PlayerBreathingSounds>("cough");
        AssertSerializedField<PlayerController>("movement");
    }

    private static void AssertSerializedField<T>(string fieldName)
    {
        FieldInfo field = typeof(T).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.That(
            field,
            Is.Not.Null,
            $"{typeof(T).Name} has no field called '{fieldName}', so the " +
            "player setup window would stop warning about it rather than " +
            "start warning about something else.");
    }

    // The enemy's manners live in these four assets, not only in her code.
    //
    // Whether she exclaims at a noise is decided by its kind: footsteps and
    // breathing are rhythms she walks towards in silence, a cough is an event
    // she is allowed to notice out loud. That decision reads sourceType off
    // the preset - so somebody changing one in the inspector changes how she
    // behaves, with nothing logged and nothing to see.
    //
    // The kinds are asserted rather than the radii and loudness, which are
    // tuning and belong to whoever is tuning them.
    [Test]
    public void PlayerNoisePresets_KeepTheKindsTheEnemyReadsThemBy()
    {
        AssertNoiseKind(
            "Assets/Collaborators/Qewzdl/Configs/Noise/Noise_FootstepRunning.asset",
            GameplayNoiseSourceType.Footstep,
            "she would exclaim at every stride of a chase.");

        AssertNoiseKind(
            "Assets/Collaborators/Qewzdl/Configs/Noise/Noise_FootstepWalking.asset",
            GameplayNoiseSourceType.Footstep,
            "she would exclaim at every step anybody takes.");

        AssertNoiseKind(
            "Assets/Collaborators/Qewzdl/Configs/Noise/Noise_Breath.asset",
            GameplayNoiseSourceType.Breath,
            "she would exclaim for as long as somebody is out of breath.");

        // The one that is meant to be an event. A cough as Footstep or Breath
        // would be heard and walked towards, but never remarked on - which is
        // the whole reason a cough is worth having.
        AssertNoiseKind(
            "Assets/Collaborators/Qewzdl/Configs/Noise/Noise_Cough.asset",
            GameplayNoiseSourceType.Player,
            "the loudest thing a player can do by accident would pass " +
            "without her reacting to it.");
    }

    private static void AssertNoiseKind(
        string path,
        GameplayNoiseSourceType expected,
        string consequence)
    {
        GameplayNoisePreset preset = LoadRequiredAsset<GameplayNoisePreset>(path);

        Assert.That(
            preset.SourceType,
            Is.EqualTo(expected),
            $"{System.IO.Path.GetFileName(path)} is a {preset.SourceType} " +
            $"rather than a {expected}, so {consequence}");

        Assert.That(
            preset.IsValid,
            Is.True,
            $"{System.IO.Path.GetFileName(path)} would be refused by the " +
            "emitter, so the noise never reaches anybody.");
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
