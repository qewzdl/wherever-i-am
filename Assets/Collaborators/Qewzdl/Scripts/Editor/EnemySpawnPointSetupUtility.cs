using UnityEditor;
using UnityEngine;

// Placing an enemy meant an empty object, a component, a position and then
// remembering it has to live under the map root. This does that, the way
// Room Volume and Hiding Place already do it.
public static class EnemySpawnPointSetupUtility
{
    [MenuItem("GameObject/Wherever I Am/Enemy Spawn Point", false, 12)]
    private static void CreateFromGameObjectMenu(MenuCommand command)
    {
        GameObject context = command.context as GameObject;
        EnemySpawnPoint spawnPoint = CreateInScene(
            context != null ? context.transform : null);

        Selection.activeGameObject = spawnPoint.gameObject;
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    // Puts the point where it will actually be used. Right-clicking a map root
    // or any object under one parents it there; otherwise it looks for a map
    // root in the open scenes, and only falls back to the scene view pivot
    // when there is none.
    public static EnemySpawnPoint CreateInScene(Transform parent = null)
    {
        Transform mapRoot = ResolveMapRoot(parent);

        GameObject root = new("Enemy Spawn");
        Undo.RegisterCreatedObjectUndo(root, "Create Enemy Spawn Point");

        Vector3 position = GetSceneViewPivot();

        if (mapRoot != null)
        {
            Undo.SetTransformParent(root.transform, mapRoot, "Parent Enemy Spawn Point");
        }

        root.transform.position = position;
        root.transform.rotation = Quaternion.identity;

        // Left without an enemy on purpose: which one belongs here is the map's
        // decision, and guessing it would be wrong on the first map that wants
        // two kinds. The gizmo draws grey until it is filled in.
        Undo.AddComponent<EnemySpawnPoint>(root);

        // Named after where it sits, so a map with several reads as a list of
        // places rather than a row of identical entries.
        root.name = $"Enemy Spawn ({position.x:0.#}, {position.z:0.#})";

        return root.GetComponent<EnemySpawnPoint>();
    }

    [MenuItem("CONTEXT/EnemySpawnPoint/Face Scene View", false, 100)]
    private static void FaceSceneViewFromContextMenu(MenuCommand command)
    {
        if (command.context is not EnemySpawnPoint spawnPoint)
        {
            return;
        }

        SceneView view = SceneView.lastActiveSceneView;

        if (view == null || view.camera == null)
        {
            return;
        }

        // An enemy appears facing somewhere, and the rotation of an empty
        // object is not something you can see. This aims it where you are
        // looking from.
        Vector3 toCamera = view.camera.transform.position - spawnPoint.transform.position;
        toCamera.y = 0f;

        if (toCamera.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Undo.RecordObject(spawnPoint.transform, "Face Enemy Spawn Point");
        spawnPoint.transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
    }

    private static Transform ResolveMapRoot(Transform parent)
    {
        if (parent != null)
        {
            GameMapRoot parentMapRoot = parent.GetComponentInParent<GameMapRoot>();
            return parentMapRoot != null ? parentMapRoot.transform : parent;
        }

        GameMapRoot mapRoot = Object.FindFirstObjectByType<GameMapRoot>();
        return mapRoot != null ? mapRoot.transform : null;
    }

    private static Vector3 GetSceneViewPivot()
    {
        SceneView view = SceneView.lastActiveSceneView;
        return view != null ? view.pivot : Vector3.zero;
    }
}
