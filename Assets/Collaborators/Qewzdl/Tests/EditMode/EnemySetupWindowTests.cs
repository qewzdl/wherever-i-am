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

    [SetUp]
    public void SetUp()
    {
        projectList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(EnemyFactory.NetworkPrefabsPath);
        projectEntries = new HashSet<NetworkPrefab>(projectList.PrefabList);
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

    // The rule that makes a second kind of enemy possible at all: an enemy with
    // its own catalog plays the difficulty from it, not the lobby's config.
    // Before this the lobby's config - one enemy's tuning - went to every enemy
    // that spawned, and a new enemy would have played as the old one.
    [Test]
    public void ChooseDifficultyConfig_PrefersTheEnemysOwnCatalogOverTheLobbys()
    {
        EnemyConfig ownEasy = Make<EnemyConfig>();
        EnemyConfig ownHard = Make<EnemyConfig>();
        EnemyConfig lobby = Make<EnemyConfig>();
        EnemyConfig prefab = Make<EnemyConfig>();

        EnemyDifficultyCatalog own = MakeCatalog((0, "Easy", ownEasy), (2, "Hard", ownHard));

        Assert.That(
            NetworkEnemyController.ChooseDifficultyConfig(own, 2, lobby, prefab),
            Is.SameAs(ownHard),
            "An enemy with its own catalog played the lobby's config instead.");

        Assert.That(
            NetworkEnemyController.ChooseDifficultyConfig(own, 1, lobby, prefab),
            Is.SameAs(lobby),
            "A difficulty the enemy's catalog does not have should fall back " +
            "to the lobby's config.");

        Assert.That(
            NetworkEnemyController.ChooseDifficultyConfig(null, 2, lobby, prefab),
            Is.SameAs(lobby),
            "An enemy with no catalog should take the lobby's config, as " +
            "every enemy did before catalogs were per enemy.");

        Assert.That(
            NetworkEnemyController.ChooseDifficultyConfig(null, 2, null, prefab),
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

        // Taken already: the shipped enemy's config folder.
        Assert.That(
            EnemyFactory.ProblemWithName("Granny", configRoot, prefabRoot),
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

        NetworkEnemyController source = AssetDatabase
            .LoadAssetAtPath<GameObject>(EnemyFactory.PrefabRoot + "/Enemy.prefab")
            .GetComponent<NetworkEnemyController>();

        Assert.That(source.DifficultyCatalog, Is.Not.Null,
            "The shipped enemy has no catalog of its own to copy.");

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
                ShippedEnemy(), "Spider", root, root, registered);

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

    // The shipped enemy was set up by hand, and its catalog is the lobby's
    // own. Deleting it from here would take the lobby's difficulties with it.
    [Test]
    public void PlanDeletion_RefusesTheShippedEnemy()
    {
        EnemyFactory.DeletionPlan plan = EnemyFactory.PlanDeletion(
            ShippedEnemy(),
            EnemyFactory.ConfigRoot,
            EnemyFactory.NetworkPrefabsPath);

        Assert.That(plan.CanDelete, Is.False,
            "The shipped enemy could be deleted, and the lobby's catalog with it.");
        Assert.That(plan.Paths, Is.Empty,
            "A refused plan still listed things to delete.");
    }

    // An enemy made from another is a variant of its prefab. Deleting the one
    // underneath would leave the variant with no body, so it is refused until
    // the variant goes first - and then allowed.
    [Test]
    public void PlanDeletion_RefusesAnEnemyAnotherIsBuiltOn()
    {
        const string root = "Assets/__EnemyVariantDeleteTest";
        NetworkPrefabsList registered = Make<NetworkPrefabsList>();

        AssetDatabase.DeleteAsset(root);
        AssetDatabase.CreateFolder("Assets", "__EnemyVariantDeleteTest");

        try
        {
            EnemyFactory.Result alpha = EnemyFactory.CreateFrom(
                ShippedEnemy(), "Alpha", root, root, registered);
            EnemyFactory.Result beta = EnemyFactory.CreateFrom(
                alpha.Prefab.GetComponent<NetworkEnemyController>(),
                "Beta", root, root, registered);

            NetworkEnemyController alphaEnemy = alpha.Prefab.GetComponent<NetworkEnemyController>();

            EnemyFactory.DeletionPlan blocked = EnemyFactory.PlanDeletion(
                alphaEnemy, root, EnemyFactory.NetworkPrefabsPath);

            Assert.That(blocked.CanDelete, Is.False,
                "Alpha could be deleted while Beta is a variant of it.");
            Assert.That(blocked.Blockers.Any(reason => reason.Contains("Beta")), Is.True,
                "The refusal did not name the enemy built on it: " +
                string.Join("; ", blocked.Blockers));

            EnemyFactory.Delete(
                EnemyFactory.PlanDeletion(
                    beta.Prefab.GetComponent<NetworkEnemyController>(),
                    root,
                    EnemyFactory.NetworkPrefabsPath),
                registered,
                toTrash: false);

            Assert.That(
                EnemyFactory.PlanDeletion(alphaEnemy, root, EnemyFactory.NetworkPrefabsPath).CanDelete,
                Is.True,
                "Alpha was still refused once nothing was built on it.");
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

    // Against the real files, so a change in how Unity writes the reference
    // shows up as the shipped map no longer placing the shipped enemy.
    [Test]
    public void FindPlacements_SeesTheShippedEnemyOnTheShippedMap()
    {
        List<(string ScenePath, int SpawnPoints)> placements =
            EnemyFactory.FindPlacements(ShippedEnemy(), EnemyFactory.MapsFolder);

        Assert.That(placements, Is.Not.Empty, "No map scenes were found at all.");
        Assert.That(
            placements.Sum(placement => placement.SpawnPoints),
            Is.GreaterThan(0),
            "No map places the shipped enemy: " +
            string.Join(", ", placements.Select(p => p.ScenePath + "=" + p.SpawnPoints)));
    }

    [Test]
    public void SetSpawnPointEnemy_PutsTheEnemyOnTheSpawnPoint()
    {
        GameObject pointObject = new("Spawn point");

        try
        {
            EnemySpawnPoint point = pointObject.AddComponent<EnemySpawnPoint>();

            EnemySetupWindow.SetSpawnPointEnemy(point, ShippedEnemy());

            Assert.That(point.EnemyPrefab, Is.SameAs(ShippedEnemy()));
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
                ShippedEnemy(), "Spider", root, root, registered);

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

    // The shipped enemy's files are not all in a folder of its own, so a
    // rename could not know which ones are its to rename.
    [Test]
    public void ProblemWithRename_RefusesTheShippedEnemy()
    {
        Assert.That(
            EnemyFactory.ProblemWithRename(
                ShippedEnemy(),
                "Wolf",
                EnemyFactory.ConfigRoot,
                EnemyFactory.PrefabRoot),
            Is.Not.Null);
    }

    // A match started without a difficulty gets the catalog's default - and
    // the default's id with it. With the config alone, an enemy with a
    // catalog of its own finds no id to look up and plays the lobby's enemy.
    [Test]
    public void MapService_DefaultingTheDifficulty_AlsoSetsItsId()
    {
        GameObject serviceObject = new("Map service");

        try
        {
            GameMapService service = serviceObject.AddComponent<GameMapService>();
            EnemyDifficultyCatalog catalog = AssetDatabase.LoadAssetAtPath<EnemyDifficultyCatalog>(
                "Assets/Collaborators/Qewzdl/Configs/Enemies/EnemyDifficultyCatalog.asset");

            TestReflection.SetField(service, "difficultyCatalog", catalog);
            TestReflection.Invoke(service, "ResolveDefaultSelection");

            Assert.That(service.SelectedEnemyConfig, Is.Not.Null);
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

        List<NetworkEnemyController> enemies = EnemyFactory.FindEnemies(
            AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(EnemyFactory.NetworkPrefabsPath));

        Assert.That(enemies, Is.Not.Empty);

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

    // An entry whose prefab was deleted is found, and removing the empty
    // entries takes out that one and nothing else.
    [Test]
    public void EmptyEntries_FindsAndRemovesOnlyEntriesWhosePrefabIsGone()
    {
        NetworkPrefabsList list = Make<NetworkPrefabsList>();
        GameObject enemy = ShippedEnemy().gameObject;

        list.Add(new NetworkPrefab { Prefab = enemy });
        list.Add(new NetworkPrefab { Prefab = null });

        Assert.That(EnemyFactory.EmptyEntries(list), Has.Count.EqualTo(1));
        Assert.That(EnemyFactory.RemoveEmptyEntries(list), Is.EqualTo(1));
        Assert.That(list.PrefabList.Select(entry => entry.Prefab), Is.EqualTo(new[] { enemy }));
        Assert.That(EnemyFactory.EmptyEntries(list), Is.Empty);
    }

    private static NetworkEnemyController ShippedEnemy()
    {
        return AssetDatabase
            .LoadAssetAtPath<GameObject>(EnemyFactory.PrefabRoot + "/Enemy.prefab")
            .GetComponent<NetworkEnemyController>();
    }

    private EnemyDifficultyCatalog MakeCatalog(params (int Id, string Name, EnemyConfig Config)[] entries)
    {
        EnemyDifficultyCatalog catalog = Make<EnemyDifficultyCatalog>();
        SerializedObject serialized = new(catalog);
        SerializedProperty list = serialized.FindProperty("difficulties");

        list.arraySize = entries.Length;

        for (int i = 0; i < entries.Length; i++)
        {
            SerializedProperty entry = list.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("difficultyId").intValue = entries[i].Id;
            entry.FindPropertyRelative("displayName").stringValue = entries[i].Name;
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
