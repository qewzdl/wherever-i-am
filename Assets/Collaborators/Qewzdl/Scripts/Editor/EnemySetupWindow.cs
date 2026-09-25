using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Every kind of enemy in the game, one at a time: pick one, see every
// difficulty of it side by side, tick its behaviours on and off, see which maps
// place it.
//
// The page is the enemy and nothing else. Making, renaming and deleting enemies
// are things done TO an enemy rather than settings OF one, so they live in the
// toolbar next to the enemy picker - New beside it, the rest in its menu with
// Delete last - and never push the tuning further down the page.
//
// Tuning her meant opening an EnemyConfig per difficulty, following each of ten
// profile slots into its own asset, and holding the other three difficulties'
// numbers in your head to see whether a value was a lever or a copy. Seventeen
// behaviour modules then lived in four separate lists. This is the player's
// setup window applied to the enemy, with one thing added the player never
// needed: columns. The question somebody tuning a difficulty is always asking
// is what changes from easy to extreme, and the only way to answer it before
// was to open four assets.
//
// Nothing is owned here. Every cell is the real field on the real asset, edited
// through the same SerializedObject the inspector uses.
//
// ---------------------------------------------------------------------------
//
// Two things are easy to get wrong in a view like this and are handled on
// purpose.
//
// A profile shared by every difficulty is ONE asset. Drawn as four columns, an
// edit in the easy column would quietly change extreme as well, and the view
// would be lying about what it was doing. Shared profiles are drawn once and
// say so. A column whose asset repeats one to its left - some difficulties
// sharing and others not - is drawn but not editable, and names the column that
// owns it.
//
// The rules about what makes a behaviour list add up to an enemy come from
// EnemyBehaviorListRules, the same place ProjectAssetValidationTests gets them,
// so the window cannot tell you a list is fine that the build then rejects.
public sealed class EnemySetupWindow : EditorWindow
{
    // The lobby's catalog: which difficulties exist, and the config an enemy
    // with no catalog of its own falls back to.
    private const string CatalogPath =
        "Assets/Collaborators/Qewzdl/Configs/Enemies/EnemyDifficultyCatalog.asset";

    private const string FoldoutPrefix = "WhereverIAm.EnemySetup.";
    private const string SelectedEnemyKey = FoldoutPrefix + "SelectedEnemy";
    private const string ModulesProperty = "behaviorModules";
    private const float LabelWidth = 210f;

    // The ten profile slots on EnemyConfig, by serialized name. Ordered the way
    // somebody tuning her thinks - how she moves and what she notices, then
    // what she does about it, then the wiring underneath.
    private static readonly (string Title, string Field)[] ProfileSlots =
    {
        ("Movement", "movementProfile"),
        ("Vision", "visionProfile"),
        ("Hearing", "hearingProfile"),
        ("Investigation", "investigationProfile"),
        ("Stealth Tactics", "stealthTacticsProfile"),
        ("Patrol", "patrolProfile"),
        ("Attack Timing", "attackTimingProfile"),
        ("Attack Hit Validation", "attackHitValidationProfile"),
        ("Posture", "postureProfile"),
        ("Navigation", "navigationProfile"),
    };

    private sealed class Difficulty
    {
        public string Name;
        public EnemyConfig Config;
        public SerializedObject Serialized;
    }

    private sealed class ProfileSlot
    {
        public string Title;

        // One per difficulty, in catalog order. Null where the slot is empty.
        public readonly List<SerializedObject> Columns = new();

        // For each column, the index of the column that owns its asset - itself
        // unless an earlier column already holds the same asset.
        public readonly List<int> Owners = new();

        public bool IsShared =>
            Columns.Count > 0 &&
            Columns[0] != null &&
            Owners.All(owner => owner == 0);
    }

    private Vector2 scroll;
    private EnemyDifficultyCatalog catalog;

    private NetworkPrefabsList networkPrefabs;
    private readonly List<NetworkEnemyController> enemies = new();
    private NetworkEnemyController selected;

    // True when the selected enemy has no catalog of its own and the lobby's is
    // being shown in its place - which is also what it gets in a match.
    private bool usesLobbyCatalog;

    // Where the toolbar menu was last drawn, so the rename prompt it opens can
    // hang off it.
    private Rect enemyMenuRect;
    private const string RenameCommand = "EnemySetupRename";

    // The saved map scenes and how many of their spawn points place the
    // selected enemy. Worked out on reload: it reads every map file.
    private readonly List<(string ScenePath, int SpawnPoints)> placements = new();

    // Network prefab entries left pointing at nothing - see
    // EnemyFactory.EmptyEntries for how they come about.
    private int emptyNetworkEntries;

    private readonly List<Difficulty> difficulties = new();
    private readonly List<ProfileSlot> slots = new();
    private readonly List<EnemyBehaviorModule> modules = new();

    private readonly Dictionary<EnemyBehaviorModule, EnemyBehaviorListRules.ModuleKind> kinds =
        new();

    [MenuItem("Tools/Wherever I Am/Enemy Setup", false, 102)]
    private static void Open()
    {
        GetWindow<EnemySetupWindow>("Enemy Setup").Show();
    }

    private void OnEnable()
    {
        Reload();
    }

    // Somebody may have changed an asset in the inspector while this sat behind
    // it. Rebuilding on focus is cheaper than being wrong.
    private void OnFocus()
    {
        Reload();
    }

    // Rebuilt rather than refreshed: an asset can be reimported under a window
    // that is merely hidden, and a SerializedObject over a destroyed object
    // throws rather than going quiet.
    private void Reload()
    {
        networkPrefabs = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(
            EnemyFactory.NetworkPrefabsPath);

        enemies.Clear();
        enemies.AddRange(EnemyFactory.FindEnemies(networkPrefabs));
        emptyNetworkEntries = EnemyFactory.EmptyEntries(networkPrefabs).Count;
        selected = ResolveSelectedEnemy();

        usesLobbyCatalog = selected == null || selected.DifficultyCatalog == null;
        catalog = usesLobbyCatalog
            ? AssetDatabase.LoadAssetAtPath<EnemyDifficultyCatalog>(CatalogPath)
            : selected.DifficultyCatalog;

        difficulties.Clear();
        slots.Clear();
        modules.Clear();
        kinds.Clear();
        placements.Clear();

        if (selected != null)
        {
            placements.AddRange(EnemyFactory.FindPlacements(selected, EnemyFactory.MapsFolder));
        }

        if (catalog != null)
        {
            for (int i = 0; i < catalog.Count; i++)
            {
                if (!catalog.TryGetEntryAt(i, out EnemyDifficultyCatalog.EnemyDifficultyEntry entry))
                {
                    continue;
                }

                difficulties.Add(new Difficulty
                {
                    Name = entry.DisplayName,
                    Config = entry.Config,
                    Serialized = entry.Config != null
                        ? new SerializedObject(entry.Config)
                        : null,
                });
            }
        }

        foreach ((string title, string field) in ProfileSlots)
        {
            slots.Add(BuildSlot(title, field));
        }

        foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(EnemyBehaviorModule)}"))
        {
            EnemyBehaviorModule module = AssetDatabase.LoadAssetAtPath<EnemyBehaviorModule>(
                AssetDatabase.GUIDToAssetPath(guid));

            if (module == null)
            {
                continue;
            }

            modules.Add(module);

            // Worked out once here rather than every repaint: it installs the
            // module into a throwaway enemy to see what arrives.
            kinds[module] = EnemyBehaviorListRules.KindOf(module);
        }

        modules.Sort((a, b) =>
        {
            int byKind = kinds[a].CompareTo(kinds[b]);
            return byKind != 0 ? byKind : string.CompareOrdinal(a.name, b.name);
        });
    }

    // The enemy chosen last time, remembered by asset rather than by position
    // in the list so that a new enemy sorting in ahead of it does not change
    // which one is open. The first enemy when nothing was chosen, or the
    // chosen one has gone.
    private NetworkEnemyController ResolveSelectedEnemy()
    {
        string guid = EditorPrefs.GetString(SelectedEnemyKey, string.Empty);

        foreach (NetworkEnemyController enemy in enemies)
        {
            if (AssetGuid(enemy) == guid)
            {
                return enemy;
            }
        }

        return enemies.FirstOrDefault();
    }

    private void Select(Object enemyPrefab)
    {
        EditorPrefs.SetString(SelectedEnemyKey, AssetGuid(enemyPrefab));
        Reload();
    }

    private static string AssetGuid(Object asset)
    {
        return asset != null
            ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset))
            : string.Empty;
    }

    private ProfileSlot BuildSlot(string title, string field)
    {
        ProfileSlot slot = new() { Title = title };

        List<Object> assets = difficulties
            .Select(difficulty => difficulty.Serialized?
                .FindProperty(field)?
                .objectReferenceValue)
            .ToList();

        int[] owners = ColumnOwners(assets);
        slot.Owners.AddRange(owners);

        // A column that repeats an earlier asset draws through the owner's
        // SerializedObject, so there is one editable copy of every asset and
        // never two views of it that could disagree.
        for (int i = 0; i < assets.Count; i++)
        {
            slot.Columns.Add(owners[i] == i
                ? assets[i] != null ? new SerializedObject(assets[i]) : null
                : slot.Columns[owners[i]]);
        }

        return slot;
    }

    // Which column owns each column's asset: its own index, unless an earlier
    // column already holds the same asset, in which case that column's.
    //
    // Separate and public so it can be tested, because getting it wrong is
    // silent in the worst way. An asset shared by two difficulties and drawn
    // as two editable columns lets an edit in one quietly change the other,
    // and nothing on screen would say it had happened. An empty slot owns
    // itself: there is nothing to share.
    public static int[] ColumnOwners(IReadOnlyList<Object> assets)
    {
        int[] owners = new int[assets.Count];

        for (int i = 0; i < assets.Count; i++)
        {
            owners[i] = i;

            if (assets[i] == null)
            {
                continue;
            }

            for (int j = 0; j < i; j++)
            {
                if (assets[j] == assets[i])
                {
                    owners[i] = j;
                    break;
                }
            }
        }

        return owners;
    }

    private void OnGUI()
    {
        // A menu item runs after the menu closes, outside any drawing, where a
        // popup has nothing to hang off. It sends a command instead, and the
        // prompt opens here, at the top of a real pass.
        if (Event.current.type == EventType.ExecuteCommand &&
            Event.current.commandName == RenameCommand)
        {
            Event.current.Use();
            ShowRenamePrompt();
        }

        DrawToolbar();

        scroll = EditorGUILayout.BeginScrollView(scroll);

        if (enemies.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No enemy prefab is registered in " + EnemyFactory.NetworkPrefabsPath +
                ". An enemy has to be there to spawn, so that is where this " +
                "window looks for them.",
                MessageType.Warning);
        }
        else if (catalog == null)
        {
            EditorGUILayout.HelpBox(
                "No enemy difficulty catalog at " + CatalogPath + ".",
                MessageType.Error);
        }
        else if (difficulties.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "The difficulty catalog lists no difficulties.",
                MessageType.Warning);
        }
        else
        {
            DrawProblems();
            DrawMaps();
            DrawBehaviors();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Profiles", EditorStyles.largeLabel);

            foreach (ProfileSlot slot in slots)
            {
                DrawSlot(slot);
            }
        }

        EditorGUILayout.EndScrollView();
    }

    // Which enemy, and what to do with enemies as a whole: pick one, make a new
    // one from it, and a menu for the rest.
    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            using (new EditorGUI.DisabledScope(enemies.Count == 0))
            {
                int current = Mathf.Max(0, enemies.IndexOf(selected));
                int next = EditorGUILayout.Popup(
                    current,
                    enemies.Select(enemy => enemy.name).ToArray(),
                    EditorStyles.toolbarPopup,
                    GUILayout.Width(200f));

                if (enemies.Count > 0 && next != current)
                {
                    Select(enemies[next]);
                }
            }

            string cannotCopy = selected == null
                ? "Pick an enemy to start from."
                : selected.DifficultyCatalog == null
                    ? selected.name + " has no difficulty catalog of its own to copy."
                    : null;

            GUIContent newContent = new(
                "+ New...",
                cannotCopy ?? "A new kind of enemy, starting as a copy of " + selected.name + ".");
            Rect newRect = GUILayoutUtility.GetRect(newContent, EditorStyles.toolbarButton);

            using (new EditorGUI.DisabledScope(cannotCopy != null))
            {
                if (GUI.Button(newRect, newContent, EditorStyles.toolbarButton))
                {
                    PopupWindow.Show(newRect, NewEnemyPrompt());
                }
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button(
                    EditorGUIUtility.TrIconContent("Refresh", "Reload every asset from disk."),
                    EditorStyles.toolbarButton,
                    GUILayout.Width(28f)))
            {
                Reload();
            }

            GUIContent menuContent = EditorGUIUtility.TrIconContent("_Menu", "More for this enemy.");
            Rect menuRect = GUILayoutUtility.GetRect(
                menuContent,
                EditorStyles.toolbarButton,
                GUILayout.Width(24f));

            if (Event.current.type == EventType.Repaint)
            {
                enemyMenuRect = menuRect;
            }

            using (new EditorGUI.DisabledScope(selected == null))
            {
                if (GUI.Button(menuRect, menuContent, EditorStyles.toolbarButton))
                {
                    BuildEnemyMenu().DropDown(menuRect);
                }
            }
        }
    }

    // Finding the enemy's assets, then the two things that change what the
    // project holds - Delete last and set apart, where a slip of the mouse is
    // least likely to land on it. An enemy this window did not make cannot be
    // renamed or deleted from here, and the menu says why rather than hiding
    // the entries.
    private GenericMenu BuildEnemyMenu()
    {
        GenericMenu menu = new();

        if (selected == null)
        {
            return menu;
        }

        NetworkEnemyController enemy = selected;

        menu.AddItem(new GUIContent("Select Prefab"), false, () =>
        {
            Selection.activeObject = enemy.gameObject;
            EditorGUIUtility.PingObject(enemy.gameObject);
        });

        if (catalog != null)
        {
            EnemyDifficultyCatalog shown = catalog;

            menu.AddItem(new GUIContent("Select Difficulty Catalog"), false, () =>
            {
                Selection.activeObject = shown;
                EditorGUIUtility.PingObject(shown);
            });
        }

        menu.AddSeparator(string.Empty);

        bool managed = EnemyFactory.OwnershipProblem(enemy, EnemyFactory.ConfigRoot) == null;

        if (managed)
        {
            menu.AddItem(new GUIContent("Rename..."), false, () =>
                SendEvent(EditorGUIUtility.CommandEvent(RenameCommand)));

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Delete..."), false, DeleteEnemy);
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("Rename... (only enemies made here)"));
            menu.AddSeparator(string.Empty);
            menu.AddDisabledItem(new GUIContent("Delete... (only enemies made here)"));
        }

        return menu;
    }

    // Where the selected enemy shows up. An enemy that is registered and tuned
    // but that no map places never appears in a match, and nothing else in
    // the editor says so.
    //
    // Two halves. The saved maps, counted from their files, answer "where is
    // it". The spawn points in whatever scenes are open answer "put it here":
    // each can be switched to this enemy, and a new one added for it.
    private void DrawMaps()
    {
        if (selected == null || !Foldout("Maps", "Maps"))
        {
            return;
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (placements.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No map scenes in " + EnemyFactory.MapsFolder + ".",
                    MessageType.Warning);
            }

            foreach ((string scenePath, int spawnPoints) in placements)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        System.IO.Path.GetFileNameWithoutExtension(scenePath),
                        GUILayout.Width(LabelWidth));
                    EditorGUILayout.LabelField(
                        spawnPoints == 0
                            ? "-"
                            : spawnPoints == 1
                                ? "1 spawn point"
                                : spawnPoints + " spawn points");

                    bool open = SceneManager.GetSceneByPath(scenePath).isLoaded;

                    using (new EditorGUI.DisabledScope(open))
                    {
                        if (GUILayout.Button("Open", GUILayout.Width(60f)) &&
                            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                        {
                            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                            Reload();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }

            DrawOpenSpawnPoints();

            EditorGUILayout.HelpBox(
                "Counts are from the saved map files - save a scene to see a " +
                "change to it here.",
                MessageType.None);
        }
    }

    private void DrawOpenSpawnPoints()
    {
        EnemySpawnPoint[] spawnPoints = FindObjectsByType<EnemySpawnPoint>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .Where(point => !EditorUtility.IsPersistent(point))
            .OrderBy(point => point.gameObject.scene.name, StringComparer.Ordinal)
            .ThenBy(point => point.name, StringComparer.Ordinal)
            .ToArray();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Spawn points in open scenes", EditorStyles.miniBoldLabel);

        if (spawnPoints.Length == 0)
        {
            EditorGUILayout.LabelField("None.", EditorStyles.miniLabel);
        }

        foreach (EnemySpawnPoint point in spawnPoints)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(
                        point.gameObject.scene.name + " / " + point.name,
                        EditorStyles.label,
                        GUILayout.Width(LabelWidth)))
                {
                    Selection.activeObject = point.gameObject;
                    EditorGUIUtility.PingObject(point.gameObject);
                }

                EditorGUILayout.LabelField(
                    point.EnemyPrefab != null ? point.EnemyPrefab.name : "(no enemy)");

                using (new EditorGUI.DisabledScope(point.EnemyPrefab == selected))
                {
                    if (GUILayout.Button("Use " + selected.name, GUILayout.Width(140f)))
                    {
                        SetSpawnPointEnemy(point, selected);
                    }
                }
            }
        }

        using (new EditorGUI.DisabledScope(!AnyEditableSceneOpen()))
        {
            if (GUILayout.Button("Add a spawn point for " + selected.name))
            {
                EnemySpawnPoint point = EnemySpawnPointSetupUtility.CreateInScene();
                SetSpawnPointEnemy(point, selected);
                Selection.activeObject = point.gameObject;
                SceneView.lastActiveSceneView?.FrameSelected();
            }
        }
    }

    // Through the serialized field and the undo system, the way the inspector
    // would, so the scene is marked changed and Ctrl+Z puts the old enemy back.
    public static void SetSpawnPointEnemy(EnemySpawnPoint point, NetworkEnemyController enemy)
    {
        SerializedObject serialized = new(point);
        serialized.FindProperty("enemyPrefab").objectReferenceValue = enemy;
        serialized.ApplyModifiedProperties();
    }

    private static bool AnyEditableSceneOpen()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).isLoaded)
            {
                return true;
            }
        }

        return false;
    }

    // A new kind of enemy, made out of the one that is open. Copies its configs
    // and profiles into a folder of its own, gives it a catalog and a prefab
    // variant, and registers it to spawn - see EnemyFactory for what is copied
    // and what is shared.
    private NamePrompt NewEnemyPrompt()
    {
        NetworkEnemyController source = selected;

        return new NamePrompt(
            "New enemy from " + source.name,
            "Copies every config and profile, so it can be tuned without " +
            "touching " + source.name + ". Behaviours and the body stay shared.",
            "Create",
            string.Empty,
            name => EnemyFactory.ProblemWithName(
                name,
                EnemyFactory.ConfigRoot,
                EnemyFactory.PrefabRoot),
            name => CreateEnemy(source, name));
    }

    private void CreateEnemy(NetworkEnemyController source, string name)
    {
        try
        {
            EnemyFactory.Result result = EnemyFactory.CreateFrom(
                source,
                name,
                EnemyFactory.ConfigRoot,
                EnemyFactory.PrefabRoot,
                networkPrefabs);

            Select(result.Prefab);
            EditorGUIUtility.PingObject(result.Prefab);
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog(
                "Could not create the enemy",
                exception.Message,
                "OK");
        }

        Repaint();
    }

    // A new name for an enemy this window made - prefab, folder and the assets
    // in it together. GUIDs survive a rename, so the maps that place it and the
    // network prefab list need nothing done to them.
    private void ShowRenamePrompt()
    {
        if (selected == null)
        {
            return;
        }

        NetworkEnemyController enemy = selected;

        PopupWindow.Show(enemyMenuRect, new NamePrompt(
            "Rename " + enemy.name,
            "Renames the prefab, its folder and every asset named after it.",
            "Rename",
            enemy.name,
            name => EnemyFactory.ProblemWithRename(
                enemy,
                name,
                EnemyFactory.ConfigRoot,
                EnemyFactory.PrefabRoot),
            name => RenameEnemy(enemy, name)));
    }

    private void RenameEnemy(NetworkEnemyController enemy, string name)
    {
        try
        {
            EnemyFactory.Rename(enemy, name, EnemyFactory.ConfigRoot, EnemyFactory.PrefabRoot);
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog(
                "Could not rename " + enemy.name,
                exception.Message,
                "OK");
        }

        Reload();
        Repaint();
    }

    // Nothing is decided from the menu itself. EnemyFactory.PlanDeletion works
    // out what the enemy owns and refuses if anything else uses any of it - a
    // map that spawns it, another enemy built on its prefab - and only then,
    // after a dialog that lists every file, is anything moved, to the trash.
    private void DeleteEnemy()
    {
        if (selected == null)
        {
            return;
        }

        EnemyFactory.DeletionPlan plan;

        EditorUtility.DisplayProgressBar(
            "Delete " + selected.name,
            "Looking for anything that still uses it",
            0.5f);

        try
        {
            plan = EnemyFactory.PlanDeletion(
                selected,
                EnemyFactory.ConfigRoot,
                EnemyFactory.NetworkPrefabsPath);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (!plan.CanDelete)
        {
            const int shown = 12;

            string reasons = string.Join("\n", plan.Blockers.Take(shown));

            if (plan.Blockers.Count > shown)
            {
                reasons += "\n...and " + (plan.Blockers.Count - shown) + " more.";
            }

            EditorUtility.DisplayDialog(
                "Cannot delete " + selected.name,
                reasons,
                "OK");

            return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "Delete " + selected.name + "?",
            "These go to the trash:\n\n" + string.Join("\n", plan.Paths) +
            "\n\nand " + selected.name + " comes out of the network prefab list.",
            "Delete",
            "Cancel");

        if (!confirmed)
        {
            return;
        }

        try
        {
            EnemyFactory.Delete(plan, networkPrefabs, toTrash: true);
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog(
                "Could not delete " + selected.name,
                exception.Message,
                "OK");
        }

        EditorPrefs.DeleteKey(SelectedEnemyKey);
        Reload();
        Repaint();
    }

    // Wired wrong rather than tuned wrong, per difficulty. First on the page on
    // purpose: an empty behaviour list or a missing sense makes every number
    // below it irrelevant.
    private void DrawProblems()
    {
        if (!Foldout("Problems", "Problems"))
        {
            return;
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            bool clean = true;

            if (emptyNetworkEntries > 0)
            {
                clean = false;
                EditorGUILayout.HelpBox(
                    EnemyFactory.NetworkPrefabsPath + " has " + emptyNetworkEntries +
                    (emptyNetworkEntries == 1 ? " entry" : " entries") + " pointing at " +
                    "a prefab that no longer exists.",
                    MessageType.Error);

                if (GUILayout.Button("Remove the empty entries"))
                {
                    EnemyFactory.RemoveEmptyEntries(networkPrefabs);
                    Reload();
                    GUIUtility.ExitGUI();
                }
            }

            if (selected != null &&
                placements.Count > 0 &&
                placements.All(placement => placement.SpawnPoints == 0))
            {
                clean = false;
                EditorGUILayout.HelpBox(
                    "No map places " + selected.name + ", so it never appears " +
                    "in a match. Give it a spawn point under Maps below.",
                    MessageType.Warning);
            }

            if (usesLobbyCatalog && selected != null)
            {
                clean = false;
                EditorGUILayout.HelpBox(
                    selected.name + " has no difficulty catalog of its own, so " +
                    "it plays with whatever config the lobby's catalog resolves " +
                    "- the numbers below are the lobby's. That is right while " +
                    "it is the only kind of enemy; with two, both would play as " +
                    "the same one. Give it a catalog on its prefab.",
                    MessageType.Warning);
            }

            foreach (Difficulty difficulty in difficulties)
            {
                if (difficulty.Config == null)
                {
                    clean = false;
                    EditorGUILayout.HelpBox(
                        difficulty.Name + ": no enemy config assigned in the catalog.",
                        MessageType.Error);
                    continue;
                }

                if (!difficulty.Config.HasRequiredProfiles)
                {
                    clean = false;
                    EditorGUILayout.HelpBox(
                        difficulty.Name + ": a required profile slot is empty, " +
                        "so this enemy will refuse to start.",
                        MessageType.Error);
                }

                foreach (string problem in EnemyBehaviorListRules.ProblemsWith(difficulty.Config))
                {
                    clean = false;
                    EditorGUILayout.HelpBox(
                        difficulty.Name + " " + problem + ".",
                        MessageType.Warning);
                }
            }

            if (clean)
            {
                EditorGUILayout.LabelField(
                    "None - every difficulty adds up to an enemy, and a map places it.",
                    EditorStyles.miniLabel);
            }
        }
    }

    // Every behaviour module in the project against every difficulty. A row set
    // in bold differs between difficulties - a behaviour one of them has and
    // another does not - which is worth seeing at a glance because it is rare
    // and deliberate.
    private void DrawBehaviors()
    {
        if (!Foldout("Behaviors", "Behaviours"))
        {
            return;
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (modules.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No behaviour module assets exist in the project.",
                    MessageType.Warning);
                return;
            }

            DrawHeader(i => i);

            foreach (EnemyBehaviorModule module in modules)
            {
                bool[] membership = difficulties
                    .Select(difficulty => difficulty.Serialized != null &&
                                          IsModuleListed(difficulty.Serialized, module))
                    .ToArray();

                bool differs = membership.Distinct().Count() > 1;

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUIContent label = new(
                        module.name,
                        kinds[module] + (string.IsNullOrEmpty(module.Notes)
                            ? string.Empty
                            : " - " + module.Notes));

                    if (GUILayout.Button(
                            label,
                            differs ? EditorStyles.boldLabel : EditorStyles.label,
                            GUILayout.Width(LabelWidth)))
                    {
                        Selection.activeObject = module;
                    }

                    for (int i = 0; i < difficulties.Count; i++)
                    {
                        Difficulty difficulty = difficulties[i];

                        using (new EditorGUI.DisabledScope(difficulty.Serialized == null))
                        {
                            bool next = EditorGUILayout.Toggle(
                                membership[i],
                                GUILayout.Width(ColumnWidth));

                            if (next != membership[i] && difficulty.Serialized != null)
                            {
                                SetModuleListed(difficulty.Serialized, module, next);
                                Save(difficulty.Serialized);
                            }
                        }
                    }
                }
            }

            EditorGUILayout.HelpBox(
                "Order does not matter: a module cannot see what else is " +
                "installed. Hover a name for what kind of module it is.",
                MessageType.None);
        }
    }

    private void DrawSlot(ProfileSlot slot)
    {
        if (slot.Columns.All(column => column == null))
        {
            EditorGUILayout.HelpBox(
                slot.Title + ": no difficulty has this profile assigned.",
                MessageType.Warning);
            return;
        }

        string title = slot.IsShared
            ? slot.Title + "  (shared by every difficulty)"
            : slot.Title;

        if (!Foldout(slot.Title, title))
        {
            return;
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (slot.IsShared)
            {
                DrawShared(slot.Columns[0]);
            }
            else
            {
                DrawColumns(slot);
            }
        }
    }

    // One asset for every difficulty, drawn once, the way the inspector would.
    private void DrawShared(SerializedObject serialized)
    {
        serialized.Update();

        SerializedProperty property = serialized.GetIterator();
        bool enterChildren = true;

        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;

            if (property.propertyPath == "m_Script")
            {
                continue;
            }

            EditorGUILayout.PropertyField(property, true);
        }

        if (serialized.ApplyModifiedProperties())
        {
            Save(serialized);
        }
    }

    // A row per field, a column per difficulty. A field whose values differ
    // between difficulties is a lever and its name is in bold; one that is the
    // same everywhere is either anatomy or a copy nobody varied.
    private void DrawColumns(ProfileSlot slot)
    {
        DrawHeader(i => slot.Owners[i]);

        List<SerializedObject> owners = slot.Columns
            .Where((column, i) => column != null && slot.Owners[i] == i)
            .ToList();

        foreach (SerializedObject owner in owners)
        {
            owner.Update();
        }

        SerializedObject template = owners.FirstOrDefault();

        if (template == null)
        {
            return;
        }

        SerializedProperty iterator = template.GetIterator();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;

            if (iterator.propertyPath == "m_Script")
            {
                continue;
            }

            DrawRow(slot, iterator.propertyPath, iterator.displayName, iterator.tooltip);
        }

        foreach (SerializedObject owner in owners)
        {
            if (owner.ApplyModifiedProperties())
            {
                Save(owner);
            }
        }
    }

    private void DrawRow(ProfileSlot slot, string path, string displayName, string tooltip)
    {
        SerializedProperty first = null;
        bool differs = false;

        foreach (SerializedObject column in slot.Columns)
        {
            SerializedProperty property = column?.FindProperty(path);

            if (property == null)
            {
                continue;
            }

            if (first == null)
            {
                first = property;
            }
            else if (!SerializedProperty.DataEquals(first, property))
            {
                differs = true;
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(
                new GUIContent(displayName, tooltip),
                differs ? EditorStyles.boldLabel : EditorStyles.label,
                GUILayout.Width(LabelWidth));

            for (int i = 0; i < slot.Columns.Count; i++)
            {
                SerializedObject column = slot.Columns[i];
                SerializedProperty property = column?.FindProperty(path);

                if (property == null)
                {
                    EditorGUILayout.LabelField("-", GUILayout.Width(ColumnWidth));
                    continue;
                }

                // A column that repeats an earlier asset shows the value and
                // refuses the edit: the owning column is where that asset is
                // changed, and letting both edit it would be one asset shown as
                // two things that could be told apart.
                using (new EditorGUI.DisabledScope(slot.Owners[i] != i))
                {
                    DrawCell(column, property);
                }
            }
        }
    }

    // Lists and nested structs do not fit in a cell, so they are opened in the
    // inspector instead of squeezed.
    private void DrawCell(SerializedObject column, SerializedProperty property)
    {
        bool fitsInACell =
            property.propertyType != SerializedPropertyType.Generic &&
            !(property.isArray && property.propertyType != SerializedPropertyType.String);

        if (fitsInACell)
        {
            EditorGUILayout.PropertyField(
                property,
                GUIContent.none,
                GUILayout.Width(ColumnWidth));
            return;
        }

        string summary = property.isArray
            ? $"[{property.arraySize}]  edit..."
            : "edit...";

        if (GUILayout.Button(summary, EditorStyles.miniButton, GUILayout.Width(ColumnWidth)))
        {
            Selection.activeObject = column.targetObject;
        }
    }

    // Difficulty names across the top. A column that repeats an earlier asset
    // names the column that owns it rather than its own difficulty, so a
    // disabled column says why.
    private void DrawHeader(System.Func<int, int> ownerOf)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(LabelWidth + 4f);

            for (int i = 0; i < difficulties.Count; i++)
            {
                int owner = ownerOf(i);

                EditorGUILayout.LabelField(
                    owner == i
                        ? difficulties[i].Name
                        : "= " + difficulties[owner].Name,
                    EditorStyles.miniBoldLabel,
                    GUILayout.Width(ColumnWidth));
            }
        }
    }

    private float ColumnWidth =>
        Mathf.Max(
            60f,
            (position.width - LabelWidth - 40f) / Mathf.Max(1, difficulties.Count));

    private static bool Foldout(string key, string label)
    {
        string prefKey = FoldoutPrefix + key;
        bool open = EditorPrefs.GetBool(prefKey, true);
        bool next = EditorGUILayout.Foldout(open, label, true, EditorStyles.foldoutHeader);

        if (next != open)
        {
            EditorPrefs.SetBool(prefKey, next);
        }

        return next;
    }

    private static void Save(SerializedObject serialized)
    {
        EditorUtility.SetDirty(serialized.targetObject);
        AssetDatabase.SaveAssetIfDirty(serialized.targetObject);
    }

    // Whether a config's behaviour list contains a module.
    public static bool IsModuleListed(SerializedObject config, EnemyBehaviorModule module)
    {
        SerializedProperty list = config.FindProperty(ModulesProperty);

        if (list == null)
        {
            return false;
        }

        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == module)
            {
                return true;
            }
        }

        return false;
    }

    // Puts a module into a config's behaviour list or takes it out, and applies
    // the change. Separate from the drawing so it can be tested: a tick box
    // that corrupts the list it edits would do it silently, one config at a
    // time.
    //
    // Adding appends and never duplicates. Removing takes out every copy, so a
    // list that somehow held one twice comes out with none, which is what a
    // cleared tick box says.
    public static void SetModuleListed(
        SerializedObject config,
        EnemyBehaviorModule module,
        bool listed)
    {
        config.Update();

        SerializedProperty list = config.FindProperty(ModulesProperty);

        if (list == null || module == null)
        {
            return;
        }

        if (listed)
        {
            if (IsModuleListed(config, module))
            {
                return;
            }

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = module;
        }
        else
        {
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue != module)
                {
                    continue;
                }

                // Older editors cleared a non-null reference on the first delete
                // and only removed the slot on the second. Checking the size
                // rather than trusting one call keeps this right on both.
                int size = list.arraySize;
                list.DeleteArrayElementAtIndex(i);

                if (list.arraySize == size)
                {
                    list.DeleteArrayElementAtIndex(i);
                }
            }
        }

        // Through the undo system, so a tick box clicked by mistake comes back
        // with the same Ctrl+Z as any other edit in the editor.
        config.ApplyModifiedProperties();
    }

    // A small popup asking for a name, hung off the button that opened it: the
    // name, what is wrong with it while anything is, and one button. Enter
    // confirms, Escape or a click elsewhere closes it - so making or renaming
    // an enemy never needs a section of its own on the page.
    public sealed class NamePrompt : PopupWindowContent
    {
        private const string FieldName = "EnemySetupName";

        private readonly string title;
        private readonly string help;
        private readonly string confirmLabel;
        private readonly Func<string, string> problemOf;
        private readonly Action<string> confirm;

        private string value;
        private bool focused;

        public NamePrompt(
            string title,
            string help,
            string confirmLabel,
            string initial,
            Func<string, string> problemOf,
            Action<string> confirm)
        {
            this.title = title;
            this.help = help;
            this.confirmLabel = confirmLabel;
            this.problemOf = problemOf;
            this.confirm = confirm;
            value = initial;
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(340f, 132f);
        }

        public override void OnGUI(Rect rect)
        {
            // Read before the text field, which would otherwise take the key.
            bool enter = Event.current.type == EventType.KeyDown &&
                         (Event.current.keyCode == KeyCode.Return ||
                          Event.current.keyCode == KeyCode.KeypadEnter);

            GUILayout.Label(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(help, EditorStyles.wordWrappedMiniLabel);

            GUI.SetNextControlName(FieldName);
            value = EditorGUILayout.TextField(value);

            if (!focused)
            {
                EditorGUI.FocusTextInControl(FieldName);
                focused = true;
            }

            string problem = problemOf(value);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    problem ?? string.Empty,
                    EditorStyles.wordWrappedMiniLabel);

                using (new EditorGUI.DisabledScope(problem != null))
                {
                    bool clicked = GUILayout.Button(confirmLabel, GUILayout.Width(90f));

                    if ((clicked || enter) && problem == null)
                    {
                        editorWindow?.Close();
                        confirm(value);
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }
    }
}
