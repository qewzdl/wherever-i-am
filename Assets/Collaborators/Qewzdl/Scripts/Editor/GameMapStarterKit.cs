using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// What a map needs before anybody can play it, put in for whoever is making
// one rather than left for them to find out about.
//
// A new map was a root, one spawn point and an empty list of objectives - and
// a match will not start on a map that cannot finish, so it would not start
// at all, with nothing on the screen to say what was missing. This adds only
// what is not there: a map already furnished keeps everything it has.
//
//   - somewhere to stand, on a map with nothing in it yet;
//   - a spawn point for every player a lobby can hold;
//   - for every objective that needs a place in the scene, an entrance door
//     bound to it and a handle to open it with;
//   - Granny, standing where she can see the spawn;
//   - the navigation she walks on, built when the match starts.
//
// Everything goes in a short way from the first spawn point, so it is all in
// view and in reach on the first try. Moving it is the map maker's job.
public static class GameMapStarterKit
{
    private const string DoorPrefabPath =
        "Assets/Collaborators/6aTowKa/Prefabs/Interactable Objects Example/InteractableObjects/SuperSimpleEntranceDoor.prefab";
    private const string HandlePrefabPath =
        "Assets/Collaborators/6aTowKa/Prefabs/Interactable Objects Example/Items/SuperSimpleDoorHandle.prefab";
    private const string EnemyPrefabPath =
        "Assets/Collaborators/Qewzdl/Prefabs/Entities/Granny.prefab";
    private const string LobbyConfigPath =
        "Assets/Collaborators/Qewzdl/Configs/Lobby/LobbyConfig.asset";

    // Default, Environment, Walls and the layer after - what the shipped map
    // builds its navigation from.
    private const int NavigationLayers = 897;

    // Opens the scene if it is not, adds what is missing, saves it. What was
    // added comes back, worded for the person who asked.
    public static List<string> Complete(
        string scenePath,
        ObjectiveSequenceDefinition sequence,
        bool addFloor)
    {
        // Held by path: opening and closing scenes unloads assets nothing in
        // them holds, and a sequence read off another scene is one of those.
        string sequencePath = sequence != null ? AssetDatabase.GetAssetPath(sequence) : null;

        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool openedHere = !scene.IsValid() || !scene.isLoaded;
        SceneSetup[] previousSetup = openedHere ? EditorSceneManager.GetSceneManagerSetup() : null;

        try
        {
            if (openedHere)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

            if (!string.IsNullOrEmpty(sequencePath))
                sequence = AssetDatabase.LoadAssetAtPath<ObjectiveSequenceDefinition>(sequencePath);

            GameMapRoot root = FindMapRoot(scene);
            List<string> added = new();
            List<NetworkObject> placed = new();

            if (addFloor)
                added.Add(AddFloor(root));

            AddPlayerSpawns(root, added);
            AddObjectives(root, sequence, added, placed);
            AddEnemy(root, added);
            AddNavigation(scene, root, added);

            // Placed from prefabs, then made the scene's own, as every network
            // object in a map has to be (NetworkSceneObjectBuildIdentityGuard),
            // once the scene has the identity a saved scene has.
            foreach (NetworkObject networkObject in placed)
                NetworkSceneObjectBuildIdentityGuard.RefreshNetworkObjectIdentity(networkObject);

            if (added.Count > 0 && !EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"Failed to save map scene '{scenePath}'.");

            return added;
        }
        finally
        {
            if (openedHere)
            {
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);

                GameMapEditorUtility.RestoreScenes(previousSetup);
            }
        }
    }

    private static GameMapRoot FindMapRoot(Scene scene)
    {
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            GameMapRoot root = rootObject.GetComponentInChildren<GameMapRoot>(true);

            if (root != null)
                return root;
        }

        throw new InvalidOperationException(
            $"Scene '{scene.path}' has no {nameof(GameMapRoot)} to complete.");
    }

    private static string AddFloor(GameMapRoot root)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.SetParent(root.transform, false);
        floor.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(30f, 1f, 30f);
        return "a floor to stand on";
    }

    private static Transform FirstSpawn(GameMapRoot root)
    {
        SerializedProperty spawns = new SerializedObject(root).FindProperty("playerSpawnPoints");

        for (int i = 0; i < spawns.arraySize; i++)
        {
            if (spawns.GetArrayElementAtIndex(i).objectReferenceValue is Transform spawn)
                return spawn;
        }

        return root.transform;
    }

    // In a row beside the first, a step and a half apart, so nobody arrives
    // inside anybody else.
    private static void AddPlayerSpawns(GameMapRoot root, List<string> added)
    {
        LobbyConfig lobby = AssetDatabase.LoadAssetAtPath<LobbyConfig>(LobbyConfigPath);
        int wanted = lobby != null ? lobby.MaxPlayers : 4;

        SerializedObject serializedRoot = new(root);
        SerializedProperty spawns = serializedRoot.FindProperty("playerSpawnPoints");
        List<Transform> kept = new();

        for (int i = 0; i < spawns.arraySize; i++)
        {
            if (spawns.GetArrayElementAtIndex(i).objectReferenceValue is Transform spawn)
                kept.Add(spawn);
        }

        if (kept.Count >= wanted)
            return;

        Transform first = kept.Count > 0 ? kept[0] : null;
        Transform parent = first != null && first.parent != null ? first.parent : root.transform;

        if (first == null)
        {
            first = new GameObject("PlayerSpawn_01").transform;
            first.SetParent(parent, false);
            first.localPosition = Vector3.up;
            kept.Add(first);
        }

        int before = kept.Count;

        while (kept.Count < wanted)
        {
            Transform spawn = new GameObject($"PlayerSpawn_{kept.Count + 1:00}").transform;
            spawn.SetParent(parent, false);
            spawn.SetPositionAndRotation(
                first.position + first.right * (1.5f * kept.Count),
                first.rotation);
            kept.Add(spawn);
        }

        spawns.arraySize = kept.Count;

        for (int i = 0; i < kept.Count; i++)
            spawns.GetArrayElementAtIndex(i).objectReferenceValue = kept[i];

        serializedRoot.ApplyModifiedPropertiesWithoutUndo();
        added.Add($"{kept.Count - before} more player spawn points");
    }

    // ponytail: every objective gets an entrance door, the one kind of
    // objective the game has; a new kind adds its own case here.
    private static void AddObjectives(
        GameMapRoot root,
        ObjectiveSequenceDefinition sequence,
        List<string> added,
        List<NetworkObject> placed)
    {
        if (sequence == null)
            return;

        ObjectiveSceneBindingRegistry registry = root.ObjectiveBindingRegistry;

        if (registry == null)
        {
            GameObject objectivesRoot = new("Map Objectives");
            objectivesRoot.transform.SetParent(root.transform, false);
            registry = objectivesRoot.AddComponent<ObjectiveSceneBindingRegistry>();
            registry.ConfigureEditor(Array.Empty<ObjectiveSceneBinding>());

            SerializedObject serializedRoot = new(root);
            serializedRoot.FindProperty("objectiveBindingRegistry").objectReferenceValue = registry;
            serializedRoot.ApplyModifiedPropertiesWithoutUndo();
        }

        HashSet<ObjectiveDefinition> bound = new();

        foreach (ObjectiveSceneBinding binding in registry.GetComponentsInChildren<ObjectiveSceneBinding>(true))
            bound.Add(binding.Objective);

        Transform spawn = FirstSpawn(root);
        int doors = 0;

        for (int i = 0; i < sequence.Count; i++)
        {
            ObjectiveDefinition objective = sequence.GetObjective(i);

            if (objective == null || !objective.RequiresSceneBinding || bound.Contains(objective))
                continue;

            Vector3 doorAt = spawn.position + spawn.forward * 6f + spawn.right * (3f * doors);
            EntranceDoor door = Place<EntranceDoor>(DoorPrefabPath, doorAt, spawn.rotation, root.transform, placed);

            Place<NetworkObject>(
                HandlePrefabPath,
                spawn.position + spawn.forward * 2f - spawn.right * (2f + doors),
                spawn.rotation,
                root.transform,
                placed);

            GameObject objectiveObject = new($"Objective - {objective.name}");
            objectiveObject.transform.SetParent(registry.transform, false);
            objectiveObject.transform.position = doorAt;

            ObjectiveSceneBinding newBinding = objectiveObject.AddComponent<ObjectiveSceneBinding>();
            SetReference(newBinding, "objective", objective);

            EntranceDoorObjectiveReporter reporter = objectiveObject.AddComponent<EntranceDoorObjectiveReporter>();
            SetReference(reporter, "objectiveBinding", newBinding);
            SetReference(reporter, "entranceDoor", door);

            bound.Add(objective);
            doors++;
            added.Add($"an entrance door and its handle for '{objective.name}'");
        }
    }

    // Further off than the door and facing the spawn: she starts where she
    // can be found, not on top of anybody.
    private static void AddEnemy(GameMapRoot root, List<string> added)
    {
        if (root.EnemySpawnPoints.Count > 0)
            return;

        NetworkEnemyController granny =
            AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath)?.GetComponent<NetworkEnemyController>();

        if (granny == null)
            return;

        Transform spawn = FirstSpawn(root);
        GameObject pointsRoot = new("Enemy Spawn Points");
        pointsRoot.transform.SetParent(root.transform, false);

        GameObject point = new("EnemySpawn_01");
        point.transform.SetParent(pointsRoot.transform, false);
        point.transform.SetPositionAndRotation(
            spawn.position + spawn.forward * 12f,
            Quaternion.LookRotation(-spawn.forward, Vector3.up));
        point.AddComponent<EnemySpawnPoint>().ConfigureEditor(granny, null);

        added.Add("Granny's spawn point (no patrol route: she holds her ground until she notices something)");
    }

    // At the root of the scene, beside the map root, where builders go. One
    // surface for each way she moves - upright and crawling - so she can
    // follow a player under things too.
    private static void AddNavigation(Scene scene, GameMapRoot root, List<string> added)
    {
        SerializedObject serializedRoot = new(root);
        SerializedProperty builders = serializedRoot.FindProperty("navMeshBuilders");

        for (int i = 0; i < builders.arraySize; i++)
        {
            if (builders.GetArrayElementAtIndex(i).objectReferenceValue != null)
                return;
        }

        GameObject navigation = new("Navigation");
        SceneManager.MoveGameObjectToScene(navigation, scene);

        foreach (int agentTypeId in EnemyAgentTypes())
        {
            NavMeshSurface surface = navigation.AddComponent<NavMeshSurface>();
            surface.agentTypeID = agentTypeId;
            surface.collectObjects = CollectObjects.All;
        }

        RuntimeNavMeshBuilder builder = navigation.AddComponent<RuntimeNavMeshBuilder>();
        SerializedObject serializedBuilder = new(builder);
        serializedBuilder.FindProperty("buildMode").intValue = (int)RuntimeNavMeshBuildMode.ServerOnly;
        serializedBuilder.FindProperty("surface").objectReferenceValue = navigation.GetComponent<NavMeshSurface>();
        serializedBuilder.FindProperty("includedLayers").intValue = NavigationLayers;
        serializedBuilder.ApplyModifiedPropertiesWithoutUndo();

        builders.arraySize = 1;
        builders.GetArrayElementAtIndex(0).objectReferenceValue = builder;
        serializedRoot.ApplyModifiedPropertiesWithoutUndo();

        added.Add("navigation for Granny, built when the match starts");
    }

    private static IEnumerable<int> EnemyAgentTypes()
    {
        EnemyPostureController posture =
            AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath)?.GetComponent<EnemyPostureController>();

        if (posture == null)
        {
            yield return 0;
            yield break;
        }

        int standing = posture.GetAgentTypeIdForPosture(EnemyPosture.Standing);
        int crawling = posture.GetAgentTypeIdForPosture(EnemyPosture.Crawling);

        yield return standing;

        if (crawling != standing)
            yield return crawling;
    }

    private static T Place<T>(
        string prefabPath,
        Vector3 position,
        Quaternion rotation,
        Transform parent,
        List<NetworkObject> placed)
        where T : Component
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        if (prefab == null)
            throw new InvalidOperationException($"The starter kit is missing '{prefabPath}'.");

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
        instance.transform.SetParent(parent, true);
        instance.transform.SetPositionAndRotation(position, rotation);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        placed.AddRange(instance.GetComponentsInChildren<NetworkObject>(true));
        return instance.GetComponentInChildren<T>(true);
    }

    private static void SetReference(Component owner, string field, UnityEngine.Object value)
    {
        SerializedObject serialized = new(owner);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
