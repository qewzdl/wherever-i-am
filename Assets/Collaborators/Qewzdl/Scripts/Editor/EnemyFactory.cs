using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Makes a new kind of enemy - from nothing, or out of an existing one - with
// its own configs for every difficulty, its own profiles, its own difficulty
// catalog and presentation profile, and a prefab of its own that knows about
// all of them; renames one, finds the maps that place one, and takes one away
// again when nothing else depends on it.
//
// No enemy is special. Every one is a variant of EnemyBase - the body and the
// components every enemy has - with its own assets in a folder named after it,
// so any of them, the first included, can be copied, renamed or deleted
// without the others noticing.
//
// Copied rather than referenced wherever a value is something you would tune,
// so the new enemy can be pulled away from the one it started as without
// dragging that one along. Shared wherever it is a switch: behaviour modules
// carry no data of their own, and a second copy of "can open doors" would be a
// second asset to keep in step for nothing.
//
// The shape of the source is kept, including which profiles are shared. A
// profile the source uses for every difficulty becomes ONE copy used by every
// difficulty of the new enemy, not four copies that start identical and then
// drift - the setup window draws a shared profile once and a per-difficulty one
// as columns, and the new enemy should come out the same way.
//
// The prefab is a variant of the base rather than of the enemy it was copied
// from, so deleting that one never takes the copy's body with it.
// ponytail: a copy carries its source's assets but not changes the source made
// to the body itself - there are none while every enemy shares the base's
// body. Carry prefab overrides across when enemies get bodies of their own.
public static class EnemyFactory
{
    public const string ConfigRoot = "Assets/Collaborators/Qewzdl/Configs/Enemies";
    public const string PrefabRoot = "Assets/Collaborators/Qewzdl/Prefabs/Entities";
    public const string BasePrefabPath = PrefabRoot + "/EnemyBase.prefab";
    public const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";
    public const string MapsFolder = GameMapEditorUtility.MapsFolder;
    public const string GameDifficultiesPath =
        "Assets/Collaborators/Qewzdl/Configs/Lobby/GameDifficultyCatalog.asset";

    // The difficulties the game offers - what every enemy needs a config for,
    // and what their configs are named after.
    public static GameDifficultyCatalog GameDifficulties =>
        AssetDatabase.LoadAssetAtPath<GameDifficultyCatalog>(GameDifficultiesPath);

    public sealed class Result
    {
        public GameObject Prefab;
        public EnemyDifficultyCatalog Catalog;
        public string Folder;
    }

    // Why a name cannot be used, or null when it can. Asked before anything is
    // written, so a clash is reported rather than half-created.
    public static string ProblemWithName(string name, string configRoot, string prefabRoot)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Give the enemy a name.";
        }

        if (name.Trim() != name)
        {
            return "The name starts or ends with a space.";
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "The name has characters a file name cannot hold.";
        }

        if (AssetDatabase.IsValidFolder($"{configRoot}/{name}"))
        {
            return $"{configRoot}/{name} already exists.";
        }

        if (File.Exists($"{prefabRoot}/{name}.prefab"))
        {
            return $"{prefabRoot}/{name}.prefab already exists.";
        }

        return null;
    }

    // Everything is written, or nothing is. A failure partway through deletes
    // what was already made, so a half-built enemy never sits in the project
    // looking like a finished one.
    public static Result CreateFrom(
        NetworkEnemyController source,
        string name,
        string configRoot,
        string prefabRoot,
        NetworkPrefabsList networkPrefabs)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        string problem = ProblemWithName(name, configRoot, prefabRoot);

        if (problem != null)
        {
            throw new ArgumentException(problem, nameof(name));
        }

        EnemyDifficultyCatalog sourceCatalog = source.DifficultyCatalog;

        if (sourceCatalog == null)
        {
            throw new InvalidOperationException(
                $"{source.name} has no difficulty catalog to copy.");
        }

        string folder = $"{configRoot}/{name}";
        string prefabPath = $"{prefabRoot}/{name}.prefab";

        try
        {
            AssetDatabase.CreateFolder(configRoot, name);

            Dictionary<Object, Object> copies = new();

            // Each config with the difficulty it serves, for its name. Named
            // after the difficulty rather than after the source asset, because
            // the source's names carry the source enemy's name in them and a
            // copy called Spider_GrannyEnemyConfig_Easy says the wrong thing
            // about which enemy it belongs to.
            List<(EnemyConfig Config, string Difficulty)> sourceConfigs = new();
            GameDifficultyCatalog difficulties = GameDifficulties;

            for (int i = 0; i < sourceCatalog.Count; i++)
            {
                if (sourceCatalog.TryGetEntryAt(i, out EnemyDifficultyCatalog.EnemyDifficultyEntry entry) &&
                    entry.Config != null &&
                    sourceConfigs.All(known => known.Config != entry.Config))
                {
                    sourceConfigs.Add((entry.Config, DifficultyName(difficulties, entry.DifficultyId)));
                }
            }

            // Profiles first, so the configs can be pointed at them.
            foreach ((EnemyConfig config, string _) in sourceConfigs)
            {
                foreach (Object profile in ProfilesOf(config))
                {
                    if (!copies.ContainsKey(profile))
                    {
                        copies[profile] = Copy(profile, folder, $"{name}_{profile.name}");
                    }
                }
            }

            foreach ((EnemyConfig config, string difficulty) in sourceConfigs)
            {
                Object configCopy = Copy(config, folder, $"{name}EnemyConfig_{difficulty}");
                Retarget(configCopy, copies);
                copies[config] = configCopy;
            }

            Object catalogCopy = Copy(sourceCatalog, folder, $"{name}DifficultyCatalog");
            Retarget(catalogCopy, copies);

            Object presentation = PresentationProfileOf(source);
            Object presentationCopy = presentation != null
                ? Copy(presentation, folder, $"{name}EnemyPresentationProfile")
                : null;

            EnemyConfig defaultConfig = source.Config != null &&
                                        copies.TryGetValue(source.Config, out Object mapped)
                ? (EnemyConfig)mapped
                : difficulties != null &&
                  ((EnemyDifficultyCatalog)catalogCopy).TryGetConfig(
                      difficulties.DefaultDifficultyId,
                      out EnemyConfig fallback)
                    ? fallback
                    : null;

            GameObject prefab = SaveVariant(
                prefabPath,
                defaultConfig,
                (EnemyDifficultyCatalog)catalogCopy,
                (EnemyPresentationProfile)presentationCopy);

            Tidy(prefab.GetComponent<NetworkEnemyController>(), configRoot);
            Register(prefab, networkPrefabs);

            AssetDatabase.SaveAssets();

            return new Result
            {
                Prefab = prefab,
                Catalog = (EnemyDifficultyCatalog)catalogCopy,
                Folder = folder,
            };
        }
        catch
        {
            AssetDatabase.DeleteAsset(prefabPath);
            AssetDatabase.DeleteAsset(folder);
            throw;
        }
    }

    // A new kind of enemy from nothing: one of every profile at its code
    // defaults, a config per difficulty the game offers, a catalog, a blank
    // presentation profile and a variant of the base.
    //
    // The profiles are shared by every difficulty. A new enemy has no levers
    // yet, the window draws a shared profile once, and pulling one difficulty
    // apart later is a matter of giving it its own copy. No behaviours are
    // listed: what the enemy does is the first decision made about it, not a
    // default it inherits - the window's Problems section says so until then.
    public static Result CreateBlank(
        string name,
        string configRoot,
        string prefabRoot,
        NetworkPrefabsList networkPrefabs)
    {
        string problem = ProblemWithName(name, configRoot, prefabRoot);

        if (problem != null)
        {
            throw new ArgumentException(problem, nameof(name));
        }

        GameDifficultyCatalog difficulties = GameDifficulties;

        if (difficulties == null || difficulties.Count == 0)
        {
            throw new InvalidOperationException(
                $"The game offers no difficulties to make configs for ({GameDifficultiesPath}).");
        }

        string folder = $"{configRoot}/{name}";
        string prefabPath = $"{prefabRoot}/{name}.prefab";

        try
        {
            AssetDatabase.CreateFolder(configRoot, name);

            Dictionary<string, Object> profiles = new();

            foreach (FieldInfo field in ConfigProfileFields())
            {
                profiles[field.Name] = Create(
                    ScriptableObject.CreateInstance(field.FieldType),
                    folder,
                    $"{name}_{field.FieldType.Name}");
            }

            EnemyDifficultyCatalog catalog = ScriptableObject.CreateInstance<EnemyDifficultyCatalog>();
            List<EnemyDifficultyCatalog.EnemyDifficultyEntry> entries = new();
            EnemyConfig defaultConfig = null;

            for (int i = 0; i < difficulties.Count; i++)
            {
                if (!difficulties.TryGetAt(i, out GameDifficultyCatalog.Difficulty difficulty))
                {
                    continue;
                }

                EnemyConfig config = ScriptableObject.CreateInstance<EnemyConfig>();
                SerializedObject serialized = new(config);

                foreach (KeyValuePair<string, Object> profile in profiles)
                {
                    serialized.FindProperty(profile.Key).objectReferenceValue = profile.Value;
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
                Create(config, folder, $"{name}EnemyConfig_{difficulty.DisplayName}");
                entries.Add(new EnemyDifficultyCatalog.EnemyDifficultyEntry(difficulty.DifficultyId, config));

                if (difficulty.DifficultyId == difficulties.DefaultDifficultyId)
                {
                    defaultConfig = config;
                }
            }

            SerializedObject catalogSerialized = new(catalog);
            SerializedProperty list = catalogSerialized.FindProperty("difficulties");
            list.arraySize = entries.Count;

            for (int i = 0; i < entries.Count; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("difficultyId").intValue = entries[i].DifficultyId;
                entry.FindPropertyRelative("config").objectReferenceValue = entries[i].Config;
            }

            catalogSerialized.ApplyModifiedPropertiesWithoutUndo();
            Create(catalog, folder, $"{name}DifficultyCatalog");

            EnemyPresentationProfile presentation = (EnemyPresentationProfile)Create(
                ScriptableObject.CreateInstance<EnemyPresentationProfile>(),
                folder,
                $"{name}EnemyPresentationProfile");

            GameObject prefab = SaveVariant(prefabPath, defaultConfig ?? entries[0].Config, catalog, presentation);

            Tidy(prefab.GetComponent<NetworkEnemyController>(), configRoot);
            Register(prefab, networkPrefabs);
            AssetDatabase.SaveAssets();

            return new Result { Prefab = prefab, Catalog = catalog, Folder = folder };
        }
        catch
        {
            AssetDatabase.DeleteAsset(prefabPath);
            AssetDatabase.DeleteAsset(folder);
            throw;
        }
    }

    // The profile slots on EnemyConfig, found by type rather than listed, so a
    // slot added later gets a profile without anybody telling this.
    private static IEnumerable<FieldInfo> ConfigProfileFields()
    {
        return typeof(EnemyConfig)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(field =>
                field.IsDefined(typeof(SerializeField), inherit: false) &&
                typeof(ScriptableObject).IsAssignableFrom(field.FieldType));
    }

    private static Object Create(Object asset, string folder, string name)
    {
        string path = $"{folder}/{name}.asset";
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static EnemyPresentationProfile PresentationProfileOf(NetworkEnemyController enemy)
    {
        EnemyPresentationController presentation =
            enemy.GetComponentInChildren<EnemyPresentationController>(true);

        return presentation != null
            ? new SerializedObject(presentation).FindProperty("profile").objectReferenceValue
                as EnemyPresentationProfile
            : null;
    }

    public static GameObject BasePrefab =>
        AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);

    private static string DifficultyName(GameDifficultyCatalog difficulties, int difficultyId)
    {
        return difficulties != null &&
               difficulties.TryGet(difficultyId, out GameDifficultyCatalog.Difficulty difficulty)
            ? difficulty.DisplayName
            : "Difficulty" + difficultyId;
    }

    // Every profile a config points at: the ScriptableObjects it references
    // that are not behaviour modules. Found by walking the config rather than
    // by naming its ten slots, so an eleventh profile added later is copied
    // without anybody remembering to tell this.
    private static IEnumerable<Object> ProfilesOf(EnemyConfig config)
    {
        SerializedProperty property = new SerializedObject(config).GetIterator();

        while (property.Next(true))
        {
            if (property.propertyType == SerializedPropertyType.ObjectReference &&
                property.objectReferenceValue is ScriptableObject profile &&
                profile is not EnemyBehaviorModule &&
                profile is not EnemyConfig &&
                property.propertyPath != "m_Script")
            {
                yield return profile;
            }
        }
    }

    private static Object Copy(Object source, string folder, string name)
    {
        string from = AssetDatabase.GetAssetPath(source);
        string to = $"{folder}/{name}.asset";

        if (!AssetDatabase.CopyAsset(from, to))
        {
            throw new IOException($"Could not copy {from} to {to}.");
        }

        return AssetDatabase.LoadAssetAtPath<Object>(to);
    }

    // Points every reference in a copy at the copy of what it referred to,
    // where there is one. Anything with no copy - a behaviour module, an
    // attack effect - is left pointing where it did, which is what sharing is.
    private static void Retarget(Object copy, IReadOnlyDictionary<Object, Object> copies)
    {
        SerializedObject serialized = new(copy);
        SerializedProperty property = serialized.GetIterator();

        while (property.Next(true))
        {
            if (property.propertyType == SerializedPropertyType.ObjectReference &&
                property.objectReferenceValue != null &&
                copies.TryGetValue(property.objectReferenceValue, out Object replacement))
            {
                property.objectReferenceValue = replacement;
            }
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(copy);
    }

    public static GameObject SaveVariant(
        string path,
        EnemyConfig config,
        EnemyDifficultyCatalog catalog,
        EnemyPresentationProfile presentationProfile)
    {
        GameObject basePrefab = BasePrefab;

        if (basePrefab == null)
        {
            throw new InvalidOperationException($"There is no enemy base prefab at {BasePrefabPath}.");
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);

        try
        {
            SerializedObject controller =
                new(instance.GetComponent<NetworkEnemyController>());

            controller.FindProperty("config").objectReferenceValue = config;
            controller.FindProperty("difficultyCatalog").objectReferenceValue = catalog;
            controller.ApplyModifiedPropertiesWithoutUndo();

            EnemyPresentationController presentation =
                instance.GetComponentInChildren<EnemyPresentationController>(true);

            if (presentation != null)
            {
                SerializedObject serialized = new(presentation);
                serialized.FindProperty("profile").objectReferenceValue = presentationProfile;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            // Saving an instance of a prefab as a new asset makes a variant of
            // it, which is the point: the new enemy's body stays the base's
            // until somebody deliberately changes it.
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, path, out bool saved);

            if (!saved || prefab == null)
            {
                throw new IOException($"Could not save the enemy prefab at {path}.");
            }

            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    // A network prefab nobody registered spawns on nobody's machine, and the
    // failure arrives at runtime as a missing hash rather than here.
    private static void Register(GameObject prefab, NetworkPrefabsList networkPrefabs)
    {
        if (networkPrefabs == null || networkPrefabs.Contains(prefab))
        {
            return;
        }

        networkPrefabs.Add(new NetworkPrefab { Prefab = prefab });

        if (EditorUtility.IsPersistent(networkPrefabs))
        {
            EditorUtility.SetDirty(networkPrefabs);
            AssetDatabase.SaveAssetIfDirty(networkPrefabs);
        }
    }

    // What deleting an enemy would remove, and everything standing in the way.
    public sealed class DeletionPlan
    {
        public GameObject Prefab;
        public readonly List<string> Paths = new();
        public readonly List<string> Blockers = new();

        public bool CanDelete => Blockers.Count == 0;
    }

    // Works out what an enemy owns and whether anything else uses any of it.
    //
    // Only an enemy this factory made can be deleted: its configs, profiles
    // and catalog are all in one folder named after it, so what belongs to it
    // is known rather than guessed. An enemy set up by hand has its assets
    // wherever somebody put them, and a delete that had to guess would
    // eventually guess wrong. The base is not an enemy at all and has no
    // folder, so it cannot be deleted from here either.
    //
    // Then one rule, which covers every way deleting could break something
    // else: nothing outside the enemy may refer to anything being removed. A
    // spawn point on a map that places it, another enemy built as a variant of
    // its prefab, a config that borrowed one of its profiles - each of those
    // is a file that mentions one of its GUIDs. Unity has no way to ask what
    // refers to an asset, but every scene, prefab and asset here is text, so
    // the question can be answered by reading them. The network prefab list is
    // the one expected reference, and Delete takes the enemy out of it.
    public static DeletionPlan PlanDeletion(
        NetworkEnemyController enemy,
        string configRoot,
        string networkPrefabsPath)
    {
        DeletionPlan plan = new() { Prefab = enemy != null ? enemy.gameObject : null };

        if (enemy == null)
        {
            plan.Blockers.Add("There is no enemy to delete.");
            return plan;
        }

        string prefabPath = AssetDatabase.GetAssetPath(enemy.gameObject);
        string folder = $"{configRoot}/{enemy.name}";
        string notOwned = OwnershipProblem(enemy, configRoot);

        if (notOwned != null)
        {
            plan.Blockers.Add(notOwned + " Delete its assets by hand if you mean to.");
            return plan;
        }

        plan.Paths.Add(prefabPath);
        plan.Paths.Add(folder);

        Dictionary<string, string> owned = new()
        {
            [AssetDatabase.AssetPathToGUID(prefabPath)] = prefabPath,
        };

        foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { folder }))
        {
            owned[guid] = AssetDatabase.GUIDToAssetPath(guid);
        }

        foreach (string file in Directory.EnumerateFiles("Assets", "*", SearchOption.AllDirectories))
        {
            string path = file.Replace('\\', '/');

            if (!(path.EndsWith(".unity", StringComparison.Ordinal) ||
                  path.EndsWith(".prefab", StringComparison.Ordinal) ||
                  path.EndsWith(".asset", StringComparison.Ordinal)))
            {
                continue;
            }

            if (path == prefabPath ||
                path == networkPrefabsPath ||
                path.StartsWith(folder + "/", StringComparison.Ordinal))
            {
                continue;
            }

            string text = ReadIfText(path);

            if (text == null)
            {
                continue;
            }

            foreach (KeyValuePair<string, string> asset in owned)
            {
                if (text.Contains(asset.Key, StringComparison.Ordinal))
                {
                    plan.Blockers.Add($"{path} uses {Path.GetFileName(asset.Value)}");
                    break;
                }
            }
        }

        return plan;
    }

    // Why what belongs to an enemy cannot be known for sure, or null when it
    // can: an enemy this factory made keeps its configs, profiles and catalog
    // in one folder named after it. Anything else - the base, or an enemy set
    // up by hand - is refused by delete and rename alike rather than guessed
    // at.
    public static string OwnershipProblem(NetworkEnemyController enemy, string configRoot)
    {
        string folder = $"{configRoot}/{enemy.name}";

        if (!AssetDatabase.IsValidFolder(folder))
        {
            return $"{enemy.name} was not made by the enemy setup window: its " +
                   $"configs are not in {folder}, so what belongs to it cannot " +
                   "be known for sure.";
        }

        string catalogPath = AssetDatabase.GetAssetPath(enemy.DifficultyCatalog);

        if (enemy.DifficultyCatalog == null ||
            !catalogPath.StartsWith(folder + "/", StringComparison.Ordinal))
        {
            return $"{enemy.name}'s difficulty catalog is not in {folder}, so " +
                   "its configs are not all its own.";
        }

        return null;
    }

    // Why an enemy cannot take a new name, or null when it can.
    public static string ProblemWithRename(
        NetworkEnemyController enemy,
        string newName,
        string configRoot,
        string prefabRoot)
    {
        if (enemy == null)
        {
            return "There is no enemy to rename.";
        }

        if (newName == enemy.name)
        {
            return "That is already its name.";
        }

        return ProblemWithName(newName, configRoot, prefabRoot) ??
               OwnershipProblem(enemy, configRoot);
    }

    // Renames the prefab, the folder and every asset in it whose name starts
    // with the old name, so Spider_EnemyVisionConfig becomes Wolf_... rather
    // than a Wolf whose files all say Spider. Something added to the folder by
    // hand under another name keeps it.
    //
    // Renamed rather than copied: an asset keeps its GUID through a rename, so
    // every map that places the enemy and the network prefab list still point
    // at it without being touched.
    public static void Rename(
        NetworkEnemyController enemy,
        string newName,
        string configRoot,
        string prefabRoot)
    {
        string problem = ProblemWithRename(enemy, newName, configRoot, prefabRoot);

        if (problem != null)
        {
            throw new ArgumentException(problem, nameof(newName));
        }

        string oldName = enemy.name;
        string folder = $"{configRoot}/{oldName}";
        string prefabPath = AssetDatabase.GetAssetPath(enemy.gameObject);

        foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string file = Path.GetFileNameWithoutExtension(path);

            if (file.StartsWith(oldName, StringComparison.Ordinal))
            {
                RenameOrThrow(path, newName + file.Substring(oldName.Length));
            }
        }

        RenameOrThrow(folder, newName);
        RenameOrThrow(prefabPath, newName);

        AssetDatabase.SaveAssets();
    }

    private static void RenameOrThrow(string path, string newName)
    {
        string error = AssetDatabase.RenameAsset(path, newName);

        if (!string.IsNullOrEmpty(error))
        {
            throw new IOException($"Could not rename {path} to {newName}: {error}");
        }
    }

    // Where every file an enemy owns belongs. One layout for every enemy,
    // whether it was made here, copied, or set up by hand and tidied:
    //
    //   <Name>/<Name>DifficultyCatalog, <Name>Presentation
    //   <Name>/<Difficulty>/<Name>Config_<Difficulty>, and every profile only
    //       that difficulty uses: <Name>Vision_<Difficulty>
    //   <Name>/Shared/ every profile all difficulties use: <Name>Navigation;
    //       one some of them share is named after those: <Name>Vision_Easy-Normal
    //
    // So the folder answers the question the setup window's columns answer -
    // which numbers are one difficulty's and which are everyone's - and every
    // file starts with the enemy's name, which is what Rename relies on.
    //
    // Only files already in the enemy's folder are placed; anything it uses
    // from elsewhere, a behaviour module or an attack effect, is shared and
    // stays where it is.
    public static List<(string From, string To)> PlanTidy(
        NetworkEnemyController enemy,
        string configRoot)
    {
        List<(string From, string To)> moves = new();
        EnemyDifficultyCatalog catalog = enemy != null ? enemy.DifficultyCatalog : null;

        if (catalog == null)
        {
            return moves;
        }

        string name = enemy.name;
        string folder = $"{configRoot}/{name}";
        GameDifficultyCatalog difficulties = GameDifficulties;

        // Which difficulties use each asset, in the game's order.
        Dictionary<Object, List<string>> users = new();
        List<string> all = new();

        for (int i = 0; i < catalog.Count; i++)
        {
            if (!catalog.TryGetEntryAt(i, out EnemyDifficultyCatalog.EnemyDifficultyEntry entry) ||
                entry.Config == null)
            {
                continue;
            }

            string difficulty = DifficultyName(difficulties, entry.DifficultyId);
            all.Add(difficulty);

            foreach (Object asset in ProfilesOf(entry.Config).Prepend(entry.Config))
            {
                if (!users.TryGetValue(asset, out List<string> list))
                {
                    users[asset] = list = new List<string>();
                }

                if (!list.Contains(difficulty))
                {
                    list.Add(difficulty);
                }
            }
        }

        void Place(Object asset, string target)
        {
            string from = AssetDatabase.GetAssetPath(asset);

            if (from.StartsWith(folder + "/", StringComparison.Ordinal) && from != target)
            {
                moves.Add((from, target));
            }
        }

        foreach (KeyValuePair<Object, List<string>> use in users)
        {
            string kind = use.Key is EnemyConfig ? "Config" : KindOf(use.Key);
            List<string> by = use.Value;

            Place(use.Key, by.Count == 1
                ? $"{folder}/{by[0]}/{name}{kind}_{by[0]}.asset"
                : by.Count == all.Count
                    ? $"{folder}/Shared/{name}{kind}.asset"
                    : $"{folder}/Shared/{name}{kind}_{string.Join("-", by)}.asset");
        }

        Place(catalog, $"{folder}/{name}DifficultyCatalog.asset");

        EnemyPresentationProfile presentation = PresentationProfileOf(enemy);

        if (presentation != null)
        {
            Place(presentation, $"{folder}/{name}Presentation.asset");
        }

        return moves;
    }

    // Moves an enemy's files into the layout above. GUIDs survive a move, so
    // nothing that refers to them notices. Everything goes through a
    // temporary name first, so a file whose new name is another's old one
    // never collides with it; folders left empty are removed.
    public static void Tidy(NetworkEnemyController enemy, string configRoot)
    {
        List<(string From, string To)> moves = PlanTidy(enemy, configRoot);

        if (moves.Count == 0)
        {
            return;
        }

        string folder = $"{configRoot}/{enemy.name}";
        List<(string Temporary, string To)> staged = new();

        foreach ((string from, string to) in moves)
        {
            string temporary = $"{folder}/__tidy_{AssetDatabase.AssetPathToGUID(from)}.asset";
            MoveOrThrow(from, temporary);
            staged.Add((temporary, to));
        }

        foreach ((string temporary, string to) in staged)
        {
            EnsureFolder(Path.GetDirectoryName(to)?.Replace('\\', '/'));
            MoveOrThrow(temporary, to);
        }

        foreach (string sub in AssetDatabase.GetSubFolders(folder))
        {
            if (AssetDatabase.FindAssets(string.Empty, new[] { sub }).Length == 0)
            {
                AssetDatabase.DeleteAsset(sub);
            }
        }

        AssetDatabase.SaveAssets();
    }

    // EnemyVisionConfig is a Vision, EnemyAttackTimingConfig an AttackTiming.
    private static string KindOf(Object profile)
    {
        string type = profile.GetType().Name;

        if (type.StartsWith("Enemy", StringComparison.Ordinal))
        {
            type = type.Substring("Enemy".Length);
        }

        if (type.EndsWith("Config", StringComparison.Ordinal))
        {
            type = type.Substring(0, type.Length - "Config".Length);
        }

        return type;
    }

    private static void MoveOrThrow(string from, string to)
    {
        string error = AssetDatabase.MoveAsset(from, to);

        if (!string.IsNullOrEmpty(error))
        {
            throw new IOException($"Could not move {from} to {to}: {error}");
        }
    }

    private static void EnsureFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // Every map scene, with how many of its spawn points place this enemy -
    // including the maps that place none, since those are where somebody
    // would go to add it.
    //
    // Read from the saved scene files rather than by opening each map, which
    // would mean closing whatever somebody is working on to count.
    public static List<(string ScenePath, int SpawnPoints)> FindPlacements(
        NetworkEnemyController enemy,
        string mapsFolder)
    {
        string guid = AssetGuid(enemy);

        return AssetDatabase.FindAssets("t:Scene", new[] { mapsFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (path, CountSpawnPoints(ReadIfText(path) ?? string.Empty, guid)))
            .ToList();
    }

    // Spawn points in a scene's text that name a prefab: either written on the
    // spawn point itself, or overridden on a prefab instance that carries one.
    public static int CountSpawnPoints(string sceneText, string prefabGuid)
    {
        if (string.IsNullOrEmpty(prefabGuid))
        {
            return 0;
        }

        string guid = Regex.Escape(prefabGuid);

        return Regex.Matches(sceneText, $@"enemyPrefab: {{fileID: -?\d+, guid: {guid},").Count +
               Regex.Matches(
                   sceneText,
                   $@"propertyPath: enemyPrefab\s+value:[^\n]*\n\s+objectReference: {{fileID: -?\d+, guid: {guid},").Count;
    }

    private static string AssetGuid(Component component)
    {
        return component != null
            ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(component.gameObject))
            : string.Empty;
    }

    // Removes what the plan says the enemy owns, having taken it out of the
    // network prefab list first while the prefab still exists to be matched.
    //
    // To the trash rather than gone, from the window: a delete somebody
    // regrets should be a trip to the recycle bin, not a trip to version
    // control. The tests delete outright so as not to fill the bin.
    public static void Delete(DeletionPlan plan, NetworkPrefabsList networkPrefabs, bool toTrash)
    {
        if (plan == null || !plan.CanDelete)
        {
            throw new InvalidOperationException(
                "Refusing to delete an enemy that something still depends on.");
        }

        if (networkPrefabs != null && plan.Prefab != null)
        {
            RemoveEntries(
                networkPrefabs,
                networkPrefabs.PrefabList.Where(entry => entry.Prefab == plan.Prefab).ToList());
        }

        foreach (string path in plan.Paths)
        {
            bool removed = toTrash
                ? AssetDatabase.MoveAssetToTrash(path)
                : AssetDatabase.DeleteAsset(path);

            if (!removed)
            {
                throw new IOException($"Could not remove {path}.");
            }
        }

        AssetDatabase.SaveAssets();
    }

    // Entries in the network prefab list whose prefab is gone. Netcode adds
    // every new NetworkObject prefab to the list by itself, and when one is
    // deleted it drops the entry from the list in memory without saving it -
    // so the file keeps an entry pointing at nothing, and the next time the
    // list is loaded the hole is back.
    public static List<NetworkPrefab> EmptyEntries(NetworkPrefabsList networkPrefabs)
    {
        if (networkPrefabs == null)
        {
            return new List<NetworkPrefab>();
        }

        return networkPrefabs.PrefabList
            .Where(entry => entry != null &&
                            entry.Override == NetworkPrefabOverride.None &&
                            entry.Prefab == null)
            .ToList();
    }

    public static int RemoveEmptyEntries(NetworkPrefabsList networkPrefabs)
    {
        List<NetworkPrefab> empty = EmptyEntries(networkPrefabs);
        RemoveEntries(networkPrefabs, empty);
        return empty.Count;
    }

    private static void RemoveEntries(NetworkPrefabsList networkPrefabs, List<NetworkPrefab> entries)
    {
        foreach (NetworkPrefab entry in entries)
        {
            networkPrefabs.Remove(entry);
        }

        if (entries.Count > 0 && EditorUtility.IsPersistent(networkPrefabs))
        {
            EditorUtility.SetDirty(networkPrefabs);
            AssetDatabase.SaveAssetIfDirty(networkPrefabs);
        }
    }

    // Binary assets cannot hold a text GUID and are skipped rather than read.
    private static string ReadIfText(string path)
    {
        using (FileStream stream = File.OpenRead(path))
        {
            byte[] head = new byte[5];

            if (stream.Read(head, 0, head.Length) < head.Length ||
                head[0] != (byte)'%' || head[1] != (byte)'Y')
            {
                return null;
            }
        }

        return File.ReadAllText(path);
    }

    // Every enemy the game can spawn, for the setup window to choose between.
    //
    // Found through the network prefab list rather than by loading every prefab
    // in the project. The window rebuilds whenever it gets focus, and loading
    // every prefab each time would be felt; the list is a dozen entries. It is
    // also the right question: a prefab that is not registered cannot spawn,
    // so it is not an enemy the game can use, however it is set up.
    public static List<NetworkEnemyController> FindEnemies(NetworkPrefabsList networkPrefabs)
    {
        if (networkPrefabs == null)
        {
            return new List<NetworkEnemyController>();
        }

        // Netcode registers every NetworkObject prefab it sees, so the list also
        // holds the base - which is every enemy's body, not an enemy - and the
        // enemies the tests keep for themselves.
        return networkPrefabs.PrefabList
            .Select(entry => entry.Prefab)
            .Where(prefab => prefab != null)
            .Where(prefab =>
            {
                string path = AssetDatabase.GetAssetPath(prefab);
                return path != BasePrefabPath && !path.Contains("/Tests/");
            })
            .Select(prefab => prefab.GetComponent<NetworkEnemyController>())
            .Where(controller => controller != null)
            .Distinct()
            .OrderBy(controller => controller.name, StringComparer.Ordinal)
            .ToList();
    }
}
