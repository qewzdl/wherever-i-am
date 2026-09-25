using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

// The parts of the enemy setup window that change data or decide what may be
// edited - every one of them fails silently if it is wrong, which is the reason
// to pin them rather than trust a glance at the window - and one pass over the
// drawing itself, so a section that throws is found here rather than by the
// next person to open it.
public sealed class EnemySetupWindowTests
{
    private readonly List<Object> created = new();

    // The project's own network prefab list, as it was before the test.
    //
    // Netcode registers every new prefab with a NetworkObject in that list by
    // itself, so the enemies these tests make in a scratch folder land in it
    // whatever list the test hands the factory. Deleting the folder takes the
    // entry out of the list in memory without saving it, which left an entry
    // pointing at nothing on disk after every run.
    private NetworkPrefabsList projectList;
    private HashSet<NetworkPrefab> projectEntries;

    // What the project's own enemy folders held before the test. These tests
    // make enemies - several of them called Spider - and must only ever do it
    // in their own scratch folders; an enemy left in the real ones would turn
    // up in the setup window looking like somebody's work.
    private string[] projectEnemyFiles;

    [SetUp]
    public void SetUp()
    {
        projectList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(EnemyFactory.NetworkPrefabsPath);
        projectEntries = new HashSet<NetworkPrefab>(projectList.PrefabList);
        projectEnemyFiles = ProjectEnemyFiles();
    }

    private static string[] ProjectEnemyFiles()
    {
        return System.IO.Directory.GetFileSystemEntries(EnemyFactory.ConfigRoot)
            .Concat(System.IO.Directory.GetFileSystemEntries(EnemyFactory.PrefabRoot))
            .OrderBy(path => path, System.StringComparer.Ordinal)
            .ToArray();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object instance in created)
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance);
            }
        }

        created.Clear();

        foreach (NetworkPrefab entry in projectList.PrefabList.Where(e => !projectEntries.Contains(e)).ToList())
        {
            projectList.Remove(entry);
        }

        // Saved whether or not anything was removed here: the stale entry may
        // be only on disk, already gone from the list in memory.
        EditorUtility.SetDirty(projectList);
        AssetDatabase.SaveAssetIfDirty(projectList);

        Assert.That(
            ProjectEnemyFiles(),
            Is.EqualTo(projectEnemyFiles),
            "The test left something in, or took something out of, the project's own enemy folders.");
    }

    private T Make<T>() where T : ScriptableObject
    {
        T instance = ScriptableObject.CreateInstance<T>();
        created.Add(instance);
        return instance;
    }

    // Two difficulties sharing one asset have to be recognised as sharing it,
    // or the window draws two editable columns over one asset and an edit in
    // the first quietly changes the second.
    [Test]
    public void ColumnOwners_SharedAssetsBelongToTheirFirstColumn()
    {
        Object a = Make<EnemyChaseBehaviorModule>();
        Object b = Make<EnemyPatrolBehaviorModule>();
        Object c = Make<EnemyAttackBehaviorModule>();
        Object d = Make<EnemyStalkBehaviorModule>();

        Assert.That(
            EnemySetupWindow.ColumnOwners(new[] { a, a, a, a }),
            Is.EqualTo(new[] { 0, 0, 0, 0 }),
            "One asset for every difficulty was not recognised as shared.");

        Assert.That(
            EnemySetupWindow.ColumnOwners(new[] { a, b, c, d }),
            Is.EqualTo(new[] { 0, 1, 2, 3 }),
            "Four different assets were treated as sharing.");

        Assert.That(
            EnemySetupWindow.ColumnOwners(new[] { a, b, a, null }),
            Is.EqualTo(new[] { 0, 1, 0, 3 }),
            "A repeated asset did not point back at the column that owns it, " +
            "or an empty slot was taken for a share.");
    }

    [Test]
    public void SetModuleListed_AddsOnceAndRemovesWithoutDisturbingTheRest()
    {
        EnemyConfig config = Make<EnemyConfig>();
        EnemyBehaviorModule chase = Make<EnemyChaseBehaviorModule>();
        EnemyBehaviorModule patrol = Make<EnemyPatrolBehaviorModule>();
        EnemyBehaviorModule attack = Make<EnemyAttackBehaviorModule>();

        SerializedObject serialized = new(config);

        EnemySetupWindow.SetModuleListed(serialized, chase, true);
        EnemySetupWindow.SetModuleListed(serialized, patrol, true);
        EnemySetupWindow.SetModuleListed(serialized, attack, true);

        Assert.That(config.BehaviorModules, Is.EqualTo(new[] { chase, patrol, attack }));

        // Ticking a box that is already ticked must not add a second copy -
        // a duplicate is silent and the validator would only catch it later.
        EnemySetupWindow.SetModuleListed(serialized, patrol, true);

        Assert.That(
            config.BehaviorModules,
            Is.EqualTo(new[] { chase, patrol, attack }),
            "Ticking an already ticked module listed it twice.");

        EnemySetupWindow.SetModuleListed(serialized, patrol, false);

        Assert.That(
            config.BehaviorModules,
            Is.EqualTo(new[] { chase, attack }),
            "Unticking one module removed the wrong one, or left a hole.");

        Assert.That(EnemySetupWindow.IsModuleListed(serialized, patrol), Is.False);
        Assert.That(EnemySetupWindow.IsModuleListed(serialized, chase), Is.True);
    }

    // A cleared box says the module is not in the list, so every copy goes -
    // including a duplicate somebody dragged in by hand.
    [Test]
    public void SetModuleListed_RemovingAModuleListedTwiceRemovesBoth()
    {
        EnemyConfig config = Make<EnemyConfig>();
        EnemyBehaviorModule chase = Make<EnemyChaseBehaviorModule>();
        EnemyBehaviorModule patrol = Make<EnemyPatrolBehaviorModule>();

        SerializedObject serialized = new(config);
        SerializedProperty list = serialized.FindProperty("behaviorModules");

        list.arraySize = 3;
        list.GetArrayElementAtIndex(0).objectReferenceValue = chase;
        list.GetArrayElementAtIndex(1).objectReferenceValue = patrol;
        list.GetArrayElementAtIndex(2).objectReferenceValue = chase;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EnemySetupWindow.SetModuleListed(serialized, chase, false);

        Assert.That(
            config.BehaviorModules,
            Is.EqualTo(new[] { patrol }),
            "A module listed twice survived being unticked.");
    }

    // The window labels every module by what it contributes. Read off by
    // installing it, so a module that changed what it installs is labelled by
    // what it does now rather than by a list somebody forgot to update.
    [Test]
    public void KindOf_ClassifiesEveryShippedModuleByWhatItInstalls()
    {
        (EnemyBehaviorModule Module, EnemyBehaviorListRules.ModuleKind Kind)[] expected =
        {
            (Make<EnemyChaseBehaviorModule>(), EnemyBehaviorListRules.ModuleKind.State),
            (Make<EnemyAttackBehaviorModule>(), EnemyBehaviorListRules.ModuleKind.State),
            (Make<EnemyPatrolBehaviorModule>(), EnemyBehaviorListRules.ModuleKind.State),
            (Make<EnemyInvestigationBehaviorModule>(), EnemyBehaviorListRules.ModuleKind.State),
            (Make<EnemyStalkBehaviorModule>(), EnemyBehaviorListRules.ModuleKind.State),
            (Make<EnemyRetreatBehaviorModule>(), EnemyBehaviorListRules.ModuleKind.State),
            (Make<EnemyFlankBehaviorModule>(), EnemyBehaviorListRules.ModuleKind.State),
            (Make<EnemyAmbushBehaviorModule>(), EnemyBehaviorListRules.ModuleKind.State),
            (Make<EnemyHidingPlaceCheckModule>(), EnemyBehaviorListRules.ModuleKind.Capability),
            (Make<EnemyLookAroundModule>(), EnemyBehaviorListRules.ModuleKind.Capability),
            (Make<EnemySearchRouteModule>(), EnemyBehaviorListRules.ModuleKind.Capability),
            (Make<EnemySightModule>(), EnemyBehaviorListRules.ModuleKind.Component),
            (Make<EnemyHearingModule>(), EnemyBehaviorListRules.ModuleKind.Component),
            (Make<EnemyLiveTargetTrackingModule>(), EnemyBehaviorListRules.ModuleKind.Component),
            (Make<EnemyDoorTraversalModule>(), EnemyBehaviorListRules.ModuleKind.Component),
            (Make<EnemyItemPushingModule>(), EnemyBehaviorListRules.ModuleKind.Component),
            (Make<EnemyCrawlingModule>(), EnemyBehaviorListRules.ModuleKind.Component),
        };

        foreach ((EnemyBehaviorModule module, EnemyBehaviorListRules.ModuleKind kind) in expected)
        {
            Assert.That(
                EnemyBehaviorListRules.KindOf(module),
                Is.EqualTo(kind),
                $"{module.GetType().Name} is labelled as the wrong kind of module.");
        }
    }

    // Every enemy plays the picked difficulty from its own catalog, and its
    // prefab's config wherever its catalog has nothing to say - never another
    // enemy's tuning, since the session carries only the difficulty's id.
    [Test]
    public void ChooseDifficultyConfig_TakesTheEnemysOwnCatalogElseItsPrefabConfig()
    {
        EnemyConfig ownEasy = Make<EnemyConfig>();
        EnemyConfig ownHard = Make<EnemyConfig>();
        EnemyConfig prefab = Make<EnemyConfig>();

        EnemyDifficultyCatalog own = MakeCatalog((0, ownEasy), (2, ownHard));

        Assert.That(
            NetworkEnemyController.ChooseDifficultyConfig(own, 2, prefab),
            Is.SameAs(ownHard));

        Assert.That(
            NetworkEnemyController.ChooseDifficultyConfig(own, 1, prefab),
            Is.SameAs(prefab),
            "A difficulty the enemy's catalog does not have should play the prefab's config.");

        Assert.That(
            NetworkEnemyController.ChooseDifficultyConfig(null, 2, prefab),
            Is.SameAs(prefab),
            "An enemy with no catalog should keep its prefab's config.");

        Assert.That(
            NetworkEnemyController.ChooseDifficultyConfig(own, GameMapService.NoDifficultySelected, prefab),
            Is.SameAs(prefab),
            "With nothing chosen, an enemy should keep its prefab's config.");
    }

    [Test]
    public void ProblemWithName_RefusesNamesThatCannotBecomeAssets()
    {
        string configRoot = EnemyFactory.ConfigRoot;
        string prefabRoot = EnemyFactory.PrefabRoot;

        Assert.That(EnemyFactory.ProblemWithName("", configRoot, prefabRoot), Is.Not.Null);
        Assert.That(EnemyFactory.ProblemWithName(" Spider", configRoot, prefabRoot), Is.Not.Null);
        Assert.That(EnemyFactory.ProblemWithName("Spi:der", configRoot, prefabRoot), Is.Not.Null);

        // Taken already: the shared behaviour library's folder.
        Assert.That(
            EnemyFactory.ProblemWithName("Behaviors", configRoot, prefabRoot),
            Is.Not.Null,
            "A name whose folder already exists was allowed, so creating it " +
            "would copy into another enemy's configs.");

        Assert.That(
            EnemyFactory.ProblemWithName(
                "NotAnEnemyYet" + GUID.Generate(),
                configRoot,
                prefabRoot),
            Is.Null);
    }

    // The whole factory, run on the shipped enemy into a throwaway folder.
    //
    // Each assertion is something that would otherwise be found in a match:
    // a new enemy still pointing at the old one's profiles changes both when
    // either is tuned, one sharing the old one's network hash cannot be told
    // apart from it on the wire, and one nobody registered never spawns.
    [Test]
    public void CreateFrom_MakesAnEnemyOfItsOwnThatKeepsTheSourcesShape()
    {
        const string root = "Assets/__EnemyFactoryTest";

        NetworkEnemyController source = SourceEnemy();

        Assert.That(source.DifficultyCatalog, Is.Not.Null,
            "The test enemy has no catalog of its own to copy.");

        NetworkPrefabsList registered = Make<NetworkPrefabsList>();

        AssetDatabase.DeleteAsset(root);
        AssetDatabase.CreateFolder("Assets", "__EnemyFactoryTest");

        try
        {
            EnemyFactory.Result result = EnemyFactory.CreateFrom(
                source, "Spider", root, root, registered);

            NetworkEnemyController made =
                result.Prefab.GetComponent<NetworkEnemyController>();

            Assert.That(
                PrefabUtility.GetPrefabAssetType(result.Prefab),
                Is.EqualTo(PrefabAssetType.Variant),
                "The new enemy is not a variant, so it no longer inherits the body.");
            Assert.That(
                PrefabUtility.GetCorrespondingObjectFromSource(result.Prefab),
                Is.SameAs(EnemyFactory.BasePrefab),
                "The copy is built on the enemy it was copied from, so deleting " +
                "that one would take the copy's body with it.");

            Object presentation = PresentationProfileOf(made);
            Assert.That(presentation, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(presentation), Does.StartWith(root),
                "The copy still shares its source's presentation profile.");

            Assert.That(made.DifficultyCatalog, Is.SameAs(result.Catalog));
            Assert.That(made.DifficultyCatalog, Is.Not.SameAs(source.DifficultyCatalog));

            EnemyConfig[] sourceConfigs = ConfigsOf(source.DifficultyCatalog);
            EnemyConfig[] madeConfigs = ConfigsOf(result.Catalog);

            Assert.That(madeConfigs, Has.Length.EqualTo(sourceConfigs.Length),
                "The new catalog lost or gained a difficulty.");
            Assert.That(madeConfigs, Does.Contain(made.Config),
                "The prefab's own config is not one of its difficulties.");

            foreach (EnemyConfig config in madeConfigs)
            {
                Assert.That(AssetDatabase.GetAssetPath(config), Does.StartWith(root),
                    $"{config.name} is not the new enemy's own copy.");
            }

            // Every profile the new configs use is a copy, and the copies are
            // shared exactly as the source's were.
            foreach (string path in ProfilePaths(sourceConfigs[0]))
            {
                Object[] sourceProfiles = sourceConfigs.Select(c => Profile(c, path)).Distinct().ToArray();
                Object[] madeProfiles = madeConfigs.Select(c => Profile(c, path)).Distinct().ToArray();

                Assert.That(madeProfiles, Has.Length.EqualTo(sourceProfiles.Length),
                    $"{path} is shared differently than it was on the source.");

                foreach (Object profile in madeProfiles)
                {
                    Assert.That(AssetDatabase.GetAssetPath(profile), Does.StartWith(root),
                        $"{path} still points at the source enemy's profile, so " +
                        "tuning either enemy would change both.");
                }
            }

            // Modules are switches and are shared, not copied.
            Assert.That(madeConfigs[0].BehaviorModules,
                Is.EqualTo(sourceConfigs[0].BehaviorModules));

            Assert.That(registered.Contains(result.Prefab), Is.True,
                "The new enemy was not registered, so it would never spawn.");

            Assert.That(
                HashOf(result.Prefab),
                Is.Not.EqualTo(HashOf(source.gameObject)),
                "The new enemy has the source's network hash, so the network " +
                "cannot tell the two apart.");
        }
        finally
        {
            AssetDatabase.DeleteAsset(root);
        }
    }

    // Made, then unmade: everything the enemy owned is gone, and it is out of
    // the network prefab list, so no entry is left pointing at nothing.
    [Test]
    public void Delete_RemovesAnEnemyTheFactoryMadeAndUnregistersIt()
    {
        const string root = "Assets/__EnemyDeleteTest";
        NetworkPrefabsList registered = Make<NetworkPrefabsList>();

        AssetDatabase.DeleteAsset(root);
        AssetDatabase.CreateFolder("Assets", "__EnemyDeleteTest");

        try
        {
            EnemyFactory.Result made = EnemyFactory.CreateFrom(
                SourceEnemy(), "Spider", root, root, registered);

            EnemyFactory.DeletionPlan plan = EnemyFactory.PlanDeletion(
                made.Prefab.GetComponent<NetworkEnemyController>(),
                root,
                EnemyFactory.NetworkPrefabsPath);

            Assert.That(plan.CanDelete, Is.True,
                "An enemy nothing else uses was refused: " +
                string.Join("; ", plan.Blockers));

            EnemyFactory.Delete(plan, registered, toTrash: false);

            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(root + "/Spider.prefab"),
                Is.Null, "The prefab survived the delete.");
            Assert.That(AssetDatabase.IsValidFolder(root + "/Spider"), Is.False,
                "The config folder survived the delete.");
            Assert.That(registered.PrefabList.Any(entry => entry.Prefab == null), Is.False,
                "The network prefab list kept an entry pointing at nothing.");
            Assert.That(registered.PrefabList, Is.Empty,
                "The deleted enemy is still registered to spawn.");
        }
        finally
        {
            AssetDatabase.DeleteAsset(root);
        }
    }

    // The base is every enemy's body. It has no folder of its own and cannot
    // be deleted from here.
    [Test]
    public void PlanDeletion_RefusesTheBase()
    {
        EnemyFactory.DeletionPlan plan = EnemyFactory.PlanDeletion(
            BaseEnemy(),
            EnemyFactory.ConfigRoot,
            EnemyFactory.NetworkPrefabsPath);

        Assert.That(plan.CanDelete, Is.False, "The base every enemy is built on could be deleted.");
        Assert.That(plan.Paths, Is.Empty, "A refused plan still listed things to delete.");
    }

    // No enemy is special: the one a copy was made from can be deleted, and
    // the copy comes through whole - its own configs, its own catalog, and a
    // body that was never borrowed from the one that went. What does stop a
    // delete is a map that still places the enemy.
    [Test]
    public void Delete_TheEnemyACopyWasMadeFrom_LeavesTheCopyWhole_ButNotWhileAMapPlacesIt()
    {
        const string root = "Assets/__EnemyChainDeleteTest";
        NetworkPrefabsList registered = Make<NetworkPrefabsList>();

        AssetDatabase.DeleteAsset(root);
        AssetDatabase.CreateFolder("Assets", "__EnemyChainDeleteTest");

        try
        {
            EnemyFactory.Result alpha = EnemyFactory.CreateFrom(
                SourceEnemy(), "Alpha", root, root, registered);
            EnemyFactory.Result beta = EnemyFactory.CreateFrom(
                alpha.Prefab.GetComponent<NetworkEnemyController>(),
                "Beta", root, root, registered);

            NetworkEnemyController alphaEnemy = alpha.Prefab.GetComponent<NetworkEnemyController>();

            // A map that places Alpha, as far as the delete can tell: a scene
            // file whose spawn point names Alpha's prefab. Written to disk
            // rather than built as a scene, which a batch run cannot do beside
            // the untitled scene the tests run in; the delete reads files.
            string scenePath = root + "/Map.unity";
            System.IO.File.WriteAllText(
                scenePath,
                "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &1\nMonoBehaviour:\n" +
                "  enemyPrefab: {fileID: 0, guid: " +
                AssetDatabase.AssetPathToGUID(root + "/Alpha.prefab") + ", type: 3}\n");

            EnemyFactory.DeletionPlan blocked = EnemyFactory.PlanDeletion(
                alphaEnemy, root, EnemyFactory.NetworkPrefabsPath);

            Assert.That(blocked.CanDelete, Is.False, "Alpha could be deleted while a map places it.");
            Assert.That(blocked.Blockers.Any(reason => reason.Contains("Map.unity")), Is.True,
                "The refusal did not name the map: " + string.Join("; ", blocked.Blockers));

            System.IO.File.Delete(scenePath);

            EnemyFactory.DeletionPlan plan = EnemyFactory.PlanDeletion(
                alphaEnemy, root, EnemyFactory.NetworkPrefabsPath);

            Assert.That(plan.CanDelete, Is.True,
                "Alpha was refused although only a copy made from it remains: " +
                string.Join("; ", plan.Blockers));

            EnemyFactory.Delete(plan, registered, toTrash: false);

            NetworkEnemyController survivor = AssetDatabase
                .LoadAssetAtPath<GameObject>(root + "/Beta.prefab")
                .GetComponent<NetworkEnemyController>();

            Assert.That(survivor.DifficultyCatalog, Is.SameAs(beta.Catalog));
            Assert.That(survivor.Config, Is.Not.Null);
            Assert.That(PresentationProfileOf(survivor), Is.Not.Null);
            Assert.That(
                PrefabUtility.GetCorrespondingObjectFromSource(survivor.gameObject),
                Is.SameAs(EnemyFactory.BasePrefab));
        }
        finally
        {
            AssetDatabase.DeleteAsset(root);
        }
    }

    // Every enemy comes out in the one layout: a folder per difficulty with its
    // config and whatever only it uses, Shared for what every difficulty
    // uses, catalog and presentation at the top. A copy of an enemy that
    // shares some profiles and not others lands each where its use says.
    [Test]
    public void CreatedEnemies_ComeOutInTheOneFolderLayout()
    {
        const string root = "Assets/__EnemyLayoutTest";
        NetworkPrefabsList registered = Make<NetworkPrefabsList>();

        AssetDatabase.DeleteAsset(root);
        AssetDatabase.CreateFolder("Assets", "__EnemyLayoutTest");

        try
        {
            EnemyFactory.Result copy = EnemyFactory.CreateFrom(SourceEnemy(), "Crow", root, root, registered);
            EnemyFactory.Result blank = EnemyFactory.CreateBlank("Moth", root, root, registered);

            foreach (EnemyFactory.Result made in new[] { copy, blank })
            {
                NetworkEnemyController enemy = made.Prefab.GetComponent<NetworkEnemyController>();

                Assert.That(EnemyFactory.PlanTidy(enemy, root), Is.Empty,
                    $"{enemy.name} did not come out tidy.");
                Assert.That(AssetDatabase.GetAssetPath(made.Catalog),
                    Is.EqualTo($"{root}/{enemy.name}/{enemy.name}DifficultyCatalog.asset"));
                Assert.That(AssetDatabase.GetAssetPath(PresentationProfileOf(enemy)),
                    Is.EqualTo($"{root}/{enemy.name}/{enemy.name}Presentation.asset"));
                Assert.That(made.Catalog.TryGetConfig(EnemyFactory.GameDifficulties.DefaultDifficultyId, out EnemyConfig normal));
                Assert.That(AssetDatabase.GetAssetPath(normal),
                    Is.EqualTo($"{root}/{enemy.name}/Normal/{enemy.name}Config_Normal.asset"));
            }

            // The blank enemy shares every profile; the copy keeps the test
            // enemy's per-difficulty vision and shared navigation.
            EnemyConfig moth = ConfigsOf(blank.Catalog)[0];
            Assert.That(AssetDatabase.GetAssetPath(moth.VisionProfile),
                Is.EqualTo($"{root}/Moth/Shared/MothVision.asset"));

            copy.Catalog.TryGetConfig(0, out EnemyConfig crowEasy);
            Assert.That(AssetDatabase.GetAssetPath(crowEasy.VisionProfile),
                Is.EqualTo($"{root}/Crow/Easy/CrowVision_Easy.asset"));
            Assert.That(AssetDatabase.GetAssetPath(crowEasy.NavigationProfile),
                Is.EqualTo($"{root}/Crow/Shared/CrowNavigation.asset"));
        }
        finally
        {
            AssetDatabase.DeleteAsset(root);
        }
    }

    // A difficulty the game offers and the enemy lacks - here Hard, taken out
    // as if the game had just gained it - gets a config copied from the
    // nearest one. What every difficulty shared stays shared; what the
    // nearest one had to itself is copied, so tuning the new one leaves the
    // old one alone; and it lands in the layout.
    [Test]
    public void AddMissingDifficulties_CopiesTheNearestDifficultyIntoTheLayout()
    {
        const string root = "Assets/__EnemyMissingTest";
        NetworkPrefabsList registered = Make<NetworkPrefabsList>();
        const int hard = 2;

        AssetDatabase.DeleteAsset(root);
        AssetDatabase.CreateFolder("Assets", "__EnemyMissingTest");

        try
        {
            EnemyFactory.Result made = EnemyFactory.CreateFrom(SourceEnemy(), "Crow", root, root, registered);
            NetworkEnemyController crow = made.Prefab.GetComponent<NetworkEnemyController>();

            // The game "gains" Hard: the enemy's Hard config and everything
            // only it used go, and its entry with them.
            SerializedObject catalog = new(made.Catalog);
            SerializedProperty list = catalog.FindProperty("difficulties");

            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative("difficultyId").intValue == hard)
                {
                    list.DeleteArrayElementAtIndex(i);
                }
            }

            catalog.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.DeleteAsset(root + "/Crow/Hard");

            Assert.That(made.Catalog.TryGetConfig(hard, out _), Is.False);

            List<string> added = EnemyFactory.AddMissingDifficulties(crow, root);

            Assert.That(added, Is.EqualTo(new[] { "Hard" }));
            Assert.That(made.Catalog.CoversEvery(EnemyFactory.GameDifficulties, out string missing), Is.True,
                "Still missing " + missing);

            made.Catalog.TryGetConfig(hard, out EnemyConfig newHard);
            made.Catalog.TryGetConfig(hard - 1, out EnemyConfig normal);

            Assert.That(newHard, Is.Not.SameAs(normal));
            Assert.That(AssetDatabase.GetAssetPath(newHard), Is.EqualTo(root + "/Crow/Hard/CrowConfig_Hard.asset"));

            // Normal's own vision was copied; the navigation everyone shares was not.
            Assert.That(newHard.VisionProfile, Is.Not.SameAs(normal.VisionProfile));
            Assert.That(newHard.detectionRadius, Is.EqualTo(normal.detectionRadius));
            Assert.That(newHard.NavigationProfile, Is.SameAs(normal.NavigationProfile));

            Assert.That(EnemyFactory.PlanTidy(crow, root), Is.Empty);
            Assert.That(EnemyFactory.AddMissingDifficulties(crow, root), Is.Empty,
                "Asked again, it added a difficulty that was no longer missing.");
        }
        finally
        {
            AssetDatabase.DeleteAsset(root);
        }
    }

    // From nothing: a variant of the base with one config per difficulty the
    // game offers, sharing one set of fresh profiles, a presentation profile
    // of its own, and no behaviours - which the rules then point out.
    [Test]
    public void CreateBlank_MakesAnEnemyFromNothingForEveryDifficultyTheGameOffers()
    {
        const string root = "Assets/__EnemyBlankTest";
        NetworkPrefabsList registered = Make<NetworkPrefabsList>();
        GameDifficultyCatalog game = EnemyFactory.GameDifficulties;

        AssetDatabase.DeleteAsset(root);
        AssetDatabase.CreateFolder("Assets", "__EnemyBlankTest");

        try
        {
            EnemyFactory.Result result = EnemyFactory.CreateBlank("Moth", root, root, registered);
            NetworkEnemyController made = result.Prefab.GetComponent<NetworkEnemyController>();

            Assert.That(
                PrefabUtility.GetCorrespondingObjectFromSource(result.Prefab),
                Is.SameAs(EnemyFactory.BasePrefab));
            Assert.That(made.DifficultyCatalog, Is.SameAs(result.Catalog));
            Assert.That(result.Catalog.IsValid(out string error), Is.True, error);
            Assert.That(result.Catalog.CoversEvery(game, out string missing), Is.True,
                "No config for " + missing);

            Assert.That(result.Catalog.TryGetConfig(game.DefaultDifficultyId, out EnemyConfig normal), Is.True);
            Assert.That(made.Config, Is.SameAs(normal),
                "The prefab's own config is not the default difficulty's.");

            EnemyConfig[] configs = ConfigsOf(result.Catalog);

            Assert.That(configs, Has.Length.EqualTo(game.Count));

            foreach (string path in ProfilePaths(configs[0]))
            {
                Object[] profiles = configs.Select(c => Profile(c, path)).Distinct().ToArray();

                Assert.That(profiles, Has.Length.EqualTo(1),
                    $"{path} is not one profile shared by every difficulty.");
                Assert.That(AssetDatabase.GetAssetPath(profiles[0]), Does.StartWith(root + "/Moth/"));
            }

            Assert.That(configs.All(config => config.HasRequiredProfiles), Is.True);
            Assert.That(configs.All(config => config.BehaviorModules.Count == 0), Is.True,
                "A blank enemy came with behaviours it never chose.");
            Assert.That(EnemyBehaviorListRules.ProblemsWith(configs[0]), Is.Not.Empty,
                "An enemy with no behaviours was not called out.");

            Object presentation = PresentationProfileOf(made);
            Assert.That(AssetDatabase.GetAssetPath(presentation), Does.StartWith(root + "/Moth/"));

            Assert.That(registered.Contains(result.Prefab), Is.True);
            Assert.That(EnemyFactory.OwnershipProblem(made, root), Is.Null,
                "A blank enemy cannot be renamed or deleted from the window.");
        }
        finally
        {
            AssetDatabase.DeleteAsset(root);
        }
    }

    // Both ways a scene can say which enemy a spawn point places: on the
    // spawn point itself, or as an override on a prefab instance of one.
    // Another enemy's GUID is not counted.
    [Test]
    public void CountSpawnPoints_CountsDirectAndOverriddenReferencesToThatEnemyOnly()
    {
        const string mine = "0123456789abcdef0123456789abcdef";
        const string other = "fedcba9876543210fedcba9876543210";

        string scene =
            "  enemyPrefab: {fileID: 3244720467120631997, guid: " + mine + ", type: 3}\n" +
            "  enemyPrefab: {fileID: 3244720467120631997, guid: " + other + ", type: 3}\n" +
            "    - target: {fileID: 1, guid: aaaa, type: 3}\n" +
            "      propertyPath: enemyPrefab\n" +
            "      value: \n" +
            "      objectReference: {fileID: -42, guid: " + mine + ", type: 3}\n" +
            "      propertyPath: patrolRoute\n" +
            "      value: \n" +
            "      objectReference: {fileID: 7, guid: " + mine + ", type: 3}\n";

        Assert.That(EnemyFactory.CountSpawnPoints(scene, mine), Is.EqualTo(2));
        Assert.That(EnemyFactory.CountSpawnPoints(scene, other), Is.EqualTo(1));
        Assert.That(EnemyFactory.CountSpawnPoints(scene, string.Empty), Is.Zero);
    }

    // Against a real scene file Unity wrote, so a change in how it writes the
    // reference shows up as the tests' own scene no longer placing the tests'
    // own enemy.
    [Test]
    public void FindPlacements_SeesAnEnemyInASceneUnityWrote()
    {
        List<(string ScenePath, int SpawnPoints)> placements = EnemyFactory.FindPlacements(
            SourceEnemy(),
            "Assets/Collaborators/Qewzdl/Tests/PlayMode/Scenarios");

        Assert.That(placements, Is.Not.Empty, "No scenes were found at all.");
        Assert.That(
            placements.Sum(placement => placement.SpawnPoints),
            Is.GreaterThan(0),
            "No scene places the test enemy: " +
            string.Join(", ", placements.Select(p => p.ScenePath + "=" + p.SpawnPoints)));
    }

    [Test]
    public void SetSpawnPointEnemy_PutsTheEnemyOnTheSpawnPoint()
    {
        GameObject pointObject = new("Spawn point");

        try
        {
            EnemySpawnPoint point = pointObject.AddComponent<EnemySpawnPoint>();

            EnemySetupWindow.SetSpawnPointEnemy(point, SourceEnemy());

            Assert.That(point.EnemyPrefab, Is.SameAs(SourceEnemy()));
        }
        finally
        {
            Object.DestroyImmediate(pointObject);
        }
    }

    // A rename moves every file the enemy owns to the new name and loses
    // nothing: the GUIDs are the same, so the network list and anything else
    // that pointed at the old names points at the new ones.
    [Test]
    public void Rename_RenamesEverythingTheEnemyOwnsAndKeepsItWired()
    {
        const string root = "Assets/__EnemyRenameTest";
        NetworkPrefabsList registered = Make<NetworkPrefabsList>();

        AssetDatabase.DeleteAsset(root);
        AssetDatabase.CreateFolder("Assets", "__EnemyRenameTest");

        try
        {
            EnemyFactory.Result made = EnemyFactory.CreateFrom(
                SourceEnemy(), "Spider", root, root, registered);

            string prefabGuid = AssetDatabase.AssetPathToGUID(root + "/Spider.prefab");
            string catalogGuid = AssetDatabase.AssetPathToGUID(
                AssetDatabase.GetAssetPath(made.Catalog));

            EnemyFactory.Rename(
                made.Prefab.GetComponent<NetworkEnemyController>(),
                "Wolf",
                root,
                root);

            Assert.That(AssetDatabase.IsValidFolder(root + "/Spider"), Is.False);
            Assert.That(AssetDatabase.IsValidFolder(root + "/Wolf"), Is.True);
            Assert.That(AssetDatabase.GUIDToAssetPath(prefabGuid), Is.EqualTo(root + "/Wolf.prefab"));
            Assert.That(
                AssetDatabase.GUIDToAssetPath(catalogGuid),
                Is.EqualTo(root + "/Wolf/WolfDifficultyCatalog.asset"));

            string[] leftovers = AssetDatabase.FindAssets(string.Empty, new[] { root + "/Wolf" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !AssetDatabase.IsValidFolder(path))
                .Where(path => !System.IO.Path.GetFileName(path).StartsWith("Wolf"))
                .ToArray();

            Assert.That(leftovers, Is.Empty, "Kept the old name: " + string.Join(", ", leftovers));

            NetworkEnemyController wolf = AssetDatabase
                .LoadAssetAtPath<GameObject>(root + "/Wolf.prefab")
                .GetComponent<NetworkEnemyController>();

            Assert.That(wolf.DifficultyCatalog, Is.SameAs(made.Catalog));
            Assert.That(registered.Contains(wolf.gameObject), Is.True,
                "The renamed enemy fell out of the network prefab list.");
        }
        finally
        {
            AssetDatabase.DeleteAsset(root);
        }
    }

    // The base has no folder of its own, so a rename could not know which
    // files are its to rename - and it is not an enemy to begin with.
    [Test]
    public void ProblemWithRename_RefusesTheBase()
    {
        Assert.That(
            EnemyFactory.ProblemWithRename(
                BaseEnemy(),
                "Wolf",
                EnemyFactory.ConfigRoot,
                EnemyFactory.PrefabRoot),
            Is.Not.Null);
    }

    // A match started without a difficulty plays the game's default one. With
    // no id at all, every enemy would find nothing in its catalog and play its
    // prefab's config whatever the default was meant to be.
    [Test]
    public void MapService_WithNothingPicked_SelectsTheDefaultDifficulty()
    {
        GameObject serviceObject = new("Map service");

        try
        {
            GameMapService service = serviceObject.AddComponent<GameMapService>();
            GameDifficultyCatalog catalog = EnemyFactory.GameDifficulties;

            TestReflection.SetField(service, "difficultyCatalog", catalog);
            TestReflection.Invoke(service, "ResolveDefaultSelection");

            Assert.That(service.SelectedDifficultyId, Is.EqualTo(catalog.DefaultDifficultyId));
        }
        finally
        {
            Object.DestroyImmediate(serviceObject);
        }
    }

    // Opens the window on every enemy with every section unfolded and draws
    // it, builds its enemy menu, and draws both name prompts - new and rename
    // - with a name that is fine and one that is not. An exception while
    // drawing is logged by the editor, and a logged exception fails the test.
    //
    // The probe window is the proof the drawing happened at all: an editor
    // that could not draw windows - a batch run, say - would otherwise pass
    // this without having drawn anything.
    [Test]
    public void Window_DrawsEveryEnemyWithEverySectionOpen()
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            Assert.Ignore("Drawing a window needs a graphics device; run without -nographics.");
        }

        const string prefix = "WhereverIAm.EnemySetup.";

        List<string> keys = new() { "Maps", "Problems", "Behaviors" };
        keys.AddRange(
            ((System.ValueTuple<string, string>[])typeof(EnemySetupWindow)
                .GetField("ProfileSlots", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null))
            .Select(slot => slot.Item1));

        Dictionary<string, bool?> saved = keys.ToDictionary(
            key => key,
            key => EditorPrefs.HasKey(prefix + key) ? EditorPrefs.GetBool(prefix + key) : (bool?)null);
        string savedSelection = EditorPrefs.GetString(prefix + "SelectedEnemy", string.Empty);

        // Possibly none: the project may be between enemies, and the window has
        // to draw that too.
        List<NetworkEnemyController> enemies = EnemyFactory.FindEnemies(
            AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(EnemyFactory.NetworkPrefabsPath));

        DrawProbe probe = EditorWindow.GetWindow<DrawProbe>();
        EnemySetupWindow window = EditorWindow.GetWindow<EnemySetupWindow>();

        try
        {
            foreach (string key in keys)
            {
                EditorPrefs.SetBool(prefix + key, true);
            }

            foreach (NetworkEnemyController enemy in enemies)
            {
                EditorPrefs.SetString(
                    prefix + "SelectedEnemy",
                    AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(enemy.gameObject)));

                TestReflection.Invoke(window, "Reload");
                DrawNow(window);

                GenericMenu menu = (GenericMenu)TestReflection.Invoke(window, "BuildEnemyMenu");
                Assert.That(menu.GetItemCount(), Is.GreaterThan(0));
            }

            if (enemies.Count == 0)
            {
                TestReflection.Invoke(window, "Reload");
                DrawNow(window);
            }

            GenericMenu newMenu = (GenericMenu)TestReflection.Invoke(window, "BuildNewMenu");
            Assert.That(newMenu.GetItemCount(), Is.GreaterThan(0));

            foreach (string name in new[] { "Wolf", string.Empty })
            {
                EnemySetupWindow.NamePrompt prompt = new(
                    "Title",
                    "Help",
                    "OK",
                    name,
                    candidate => EnemyFactory.ProblemWithName(
                        candidate,
                        EnemyFactory.ConfigRoot,
                        EnemyFactory.PrefabRoot),
                    _ => Assert.Fail("Drawing the prompt confirmed it."));

                probe.Draw = () => prompt.OnGUI(probe.position);
                DrawNow(probe);
            }

            Assert.That(probe.Draws, Is.GreaterThan(0),
                "The editor drew no window at all, so nothing here was drawn either.");
        }
        finally
        {
            window.Close();
            probe.Close();

            foreach (KeyValuePair<string, bool?> key in saved)
            {
                if (key.Value.HasValue)
                {
                    EditorPrefs.SetBool(prefix + key.Key, key.Value.Value);
                }
                else
                {
                    EditorPrefs.DeleteKey(prefix + key.Key);
                }
            }

            EditorPrefs.SetString(prefix + "SelectedEnemy", savedSelection);
        }
    }

    private static void DrawNow(EditorWindow window)
    {
        typeof(EditorWindow)
            .GetMethod("RepaintImmediately", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Invoke(window, null);
    }

    public sealed class DrawProbe : EditorWindow
    {
        public int Draws;
        public System.Action Draw;

        private void OnGUI()
        {
            Draws++;
            Draw?.Invoke();
        }
    }

    // A list holding a test prefab and a game one loses the test one only.
    [Test]
    public void RemoveTestPrefabs_TakesOutOnlyWhatLivesUnderTests()
    {
        NetworkPrefabsList list = Make<NetworkPrefabsList>();
        GameObject game = EnemyFactory.BasePrefab;
        GameObject test = SourceEnemy().gameObject;

        list.Add(new NetworkPrefab { Prefab = game });
        list.Add(new NetworkPrefab { Prefab = test });

        Assert.That(TestPrefabsOutOfNetworkList.RemoveTestPrefabs(list), Is.EqualTo(1));
        Assert.That(list.PrefabList.Select(entry => entry.Prefab), Is.EqualTo(new[] { game }));
    }

    // Reimporting the tests' enemy is when Netcode adds it to the project's
    // network prefab list - checked, or this would prove nothing. Whatever
    // else happens to it in the editor, it is out before a build reads the
    // list. (The editor also cleans it once the import is over, but when
    // that runs is the editor's business and not something to wait on.)
    [Test]
    public void BeforeABuild_TheTestEnemyIsTakenBackOutOfTheNetworkList()
    {
        GameObject test = SourceEnemy().gameObject;

        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(test), ImportAssetOptions.ForceUpdate);

        Assert.That(projectList.PrefabList.Any(entry => entry.Prefab == test), Is.True,
            "Netcode no longer adds a reimported prefab to the list, so this proves nothing.");

        new TestPrefabsOutOfNetworkList().OnPreprocessBuild(null);

        Assert.That(projectList.PrefabList.Any(entry => entry.Prefab == test), Is.False,
            "The test enemy would ship with the game.");
    }

    // An entry whose prefab was deleted is found, and removing the empty
    // entries takes out that one and nothing else.
    [Test]
    public void EmptyEntries_FindsAndRemovesOnlyEntriesWhosePrefabIsGone()
    {
        NetworkPrefabsList list = Make<NetworkPrefabsList>();
        GameObject enemy = SourceEnemy().gameObject;

        list.Add(new NetworkPrefab { Prefab = enemy });
        list.Add(new NetworkPrefab { Prefab = null });

        Assert.That(EnemyFactory.EmptyEntries(list), Has.Count.EqualTo(1));
        Assert.That(EnemyFactory.RemoveEmptyEntries(list), Is.EqualTo(1));
        Assert.That(list.PrefabList.Select(entry => entry.Prefab), Is.EqualTo(new[] { enemy }));
        Assert.That(EnemyFactory.EmptyEntries(list), Is.Empty);
    }

    // The tests' own enemy - a copy of the shipped one as it was, kept with the
    // tests so that no test depends on which enemies the game has.
    private static NetworkEnemyController SourceEnemy()
    {
        return AssetDatabase
            .LoadAssetAtPath<GameObject>("Assets/Collaborators/Qewzdl/Tests/Fixtures/TestEnemy.prefab")
            .GetComponent<NetworkEnemyController>();
    }

    private static NetworkEnemyController BaseEnemy()
    {
        return EnemyFactory.BasePrefab.GetComponent<NetworkEnemyController>();
    }

    private static Object PresentationProfileOf(NetworkEnemyController enemy)
    {
        return new SerializedObject(enemy.GetComponentInChildren<EnemyPresentationController>(true))
            .FindProperty("profile").objectReferenceValue;
    }

    private EnemyDifficultyCatalog MakeCatalog(params (int Id, EnemyConfig Config)[] entries)
    {
        EnemyDifficultyCatalog catalog = Make<EnemyDifficultyCatalog>();
        SerializedObject serialized = new(catalog);
        SerializedProperty list = serialized.FindProperty("difficulties");

        list.arraySize = entries.Length;

        for (int i = 0; i < entries.Length; i++)
        {
            SerializedProperty entry = list.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("difficultyId").intValue = entries[i].Id;
            entry.FindPropertyRelative("config").objectReferenceValue = entries[i].Config;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        return catalog;
    }

    private static EnemyConfig[] ConfigsOf(EnemyDifficultyCatalog catalog)
    {
        List<EnemyConfig> configs = new();

        for (int i = 0; i < catalog.Count; i++)
        {
            if (catalog.TryGetEntryAt(i, out EnemyDifficultyCatalog.EnemyDifficultyEntry entry))
            {
                configs.Add(entry.Config);
            }
        }

        return configs.ToArray();
    }

    private static IEnumerable<string> ProfilePaths(EnemyConfig config)
    {
        SerializedProperty property = new SerializedObject(config).GetIterator();

        while (property.Next(true))
        {
            if (property.propertyType == SerializedPropertyType.ObjectReference &&
                property.objectReferenceValue is ScriptableObject reference &&
                reference is not EnemyBehaviorModule &&
                property.propertyPath != "m_Script")
            {
                yield return property.propertyPath;
            }
        }
    }

    private static Object Profile(EnemyConfig config, string path)
    {
        return new SerializedObject(config).FindProperty(path).objectReferenceValue;
    }

    private static long HashOf(GameObject prefab)
    {
        return new SerializedObject(prefab.GetComponent<NetworkObject>())
            .FindProperty("GlobalObjectIdHash")
            .longValue;
    }
}
