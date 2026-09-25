using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Makes a new kind of enemy out of an existing one - its own configs for every
// difficulty, its own copies of the profiles those configs point at, its own
// difficulty catalog, and a prefab of its own that knows about all of them -
// renames one, finds the maps that place one, and takes one away again when
// nothing else depends on it.
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
// The prefab is a variant of the source's, so the body, the components and the
// presentation are inherited and a change to the original body still reaches
// it. Its config and catalog are overridden to its own.
public static class EnemyFactory
{
    public const string ConfigRoot = "Assets/Collaborators/Qewzdl/Configs/Enemies";
    public const string PrefabRoot = "Assets/Collaborators/Qewzdl/Prefabs/Entities";
    public const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";
    public const string MapsFolder = GameMapEditorUtility.MapsFolder;

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

            for (int i = 0; i < sourceCatalog.Count; i++)
            {
                if (sourceCatalog.TryGetEntryAt(i, out EnemyDifficultyCatalog.EnemyDifficultyEntry entry) &&
                    entry.Config != null &&
                    sourceConfigs.All(known => known.Config != entry.Config))
                {
                    sourceConfigs.Add((entry.Config, entry.DisplayName));
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

            EnemyConfig defaultConfig = source.Config != null &&
                                        copies.TryGetValue(source.Config, out Object mapped)
                ? (EnemyConfig)mapped
                : ((EnemyDifficultyCatalog)catalogCopy).TryGetConfig(
                    sourceCatalog.DefaultDifficultyId,
                    out EnemyConfig fallback)
                    ? fallback
                    : null;

            GameObject prefab = SaveVariant(
                source,
                prefabPath,
                defaultConfig,
                (EnemyDifficultyCatalog)catalogCopy);

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

    private static GameObject SaveVariant(
        NetworkEnemyController source,
        string path,
        EnemyConfig config,
        EnemyDifficultyCatalog catalog)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source.gameObject);

        try
        {
            SerializedObject controller =
                new(instance.GetComponent<NetworkEnemyController>());

            controller.FindProperty("config").objectReferenceValue = config;
            controller.FindProperty("difficultyCatalog").objectReferenceValue = catalog;
            controller.ApplyModifiedPropertiesWithoutUndo();

            // Saving an instance of a prefab as a new asset makes a variant of
            // it, which is the point: the new enemy's body stays the old one's
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
    // eventually guess wrong. The shipped enemy is one of those - its catalog
    // is the lobby's own - so it cannot be deleted from here at all.
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
    // in one folder named after it. Anything else - the shipped enemy, whose
    // catalog is the lobby's own, or one set up by hand - is refused by delete
    // and rename alike rather than guessed at.
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

        return networkPrefabs.PrefabList
            .Select(entry => entry.Prefab)
            .Where(prefab => prefab != null)
            .Select(prefab => prefab.GetComponent<NetworkEnemyController>())
            .Where(controller => controller != null)
            .Distinct()
            .OrderBy(controller => controller.name, StringComparer.Ordinal)
            .ToList();
    }
}
