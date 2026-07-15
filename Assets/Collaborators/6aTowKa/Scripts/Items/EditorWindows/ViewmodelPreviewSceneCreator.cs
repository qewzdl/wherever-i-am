using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ViewmodelPreviewSceneCreator
{
    private const string ScenePath = "Assets/Collaborators/6aTowKa/Scenes/ViewmodelPreview.unity";

    public static void OpenOrCreatePreviewScene(GameObject itemPrefab = null)
    {
        if (!System.IO.File.Exists(ScenePath))
            CreateScene();
        else
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        if (itemPrefab != null)
            PlaceItemInScene(itemPrefab);
    }

    private static void PlaceItemInScene(GameObject prefab)
    {
        ViewmodelPreviewSetup setup = ResolvePreviewSetup();
        if (setup == null) return;

        var container = setup.transform;

        for (int i = container.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(container.GetChild(i).gameObject);

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, container);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale    = Vector3.one;
        Undo.RegisterCreatedObjectUndo(instance, "Place Item in Viewmodel Preview");

        setup.Refresh();

        if (setup.targetAsset != null)
            ApplySavedViewmodelTransform(instance, setup.targetAsset);

        EditorSceneManager.MarkSceneDirty(setup.gameObject.scene);
    }

    private static ViewmodelPreviewSetup ResolvePreviewSetup()
    {
        Scene scene = EditorSceneManager.GetActiveScene();

        if (!scene.IsValid() || !scene.isLoaded)
            return null;

        GameObject[] roots = scene.GetRootGameObjects();

        for (int i = 0; i < roots.Length; i++)
        {
            ViewmodelPreviewSetup setup =
                roots[i].GetComponentInChildren<ViewmodelPreviewSetup>(true);

            if (setup != null)
                return setup;
        }

        return null;
    }

    private static void ApplySavedViewmodelTransform(GameObject itemInstance, PickupItemData data)
    {
        var comp = itemInstance.GetComponentInChildren<PickupItem>(true);
        if (comp == null) return;

        var so = new SerializedObject(comp);
        var modelGO = so.FindProperty("model")?.objectReferenceValue as GameObject;
        if (modelGO == null) return;

        var vmData = data.ViewmodelData;

        // savedMatrix: model's desired local transform relative to the container
        // (matches the runtime setup where model is a direct child of viewModelContainer).
        var savedMatrix = Matrix4x4.TRS(vmData.position, Quaternion.Euler(vmData.rotation), vmData.scale);

        // modelInItemRoot: model's current local transform relative to item root.
        // Since item root is placed at identity (localPos=0) relative to container right now,
        // this equals container.worldToLocal * model.localToWorld = just the prefab-local offset.
        var modelInItemRoot = itemInstance.transform.worldToLocalMatrix * modelGO.transform.localToWorldMatrix;

        // Solve: itemMatrix * modelInItemRoot = savedMatrix  →  itemMatrix = savedMatrix * modelInItemRoot⁻¹
        var itemMatrix = savedMatrix * modelInItemRoot.inverse;

        Undo.RecordObject(itemInstance.transform, "Apply Viewmodel Transform");
        itemInstance.transform.localPosition = itemMatrix.GetColumn(3);
        itemInstance.transform.localRotation = itemMatrix.rotation;
        itemInstance.transform.localScale    = itemMatrix.lossyScale;
    }

    private static void CreateScene()
    {
        // Prompt to save current scene if dirty
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Directional light
        var lightGO = new GameObject("Directional Light");
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // Camera (first-person view)
        var cameraGO = new GameObject("Camera");
        var cam = cameraGO.AddComponent<Camera>();
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 100f;
        cameraGO.transform.position = new Vector3(0f, 1.7f, 0f);
        cameraGO.AddComponent<AudioListener>();

        // ViewmodelContainer — child of camera, at typical arm position
        var containerGO = new GameObject("ViewmodelContainer");
        containerGO.transform.SetParent(cameraGO.transform, false);
        containerGO.transform.localPosition = new Vector3(0.1f, -0.15f, 0.3f);
        containerGO.AddComponent<ViewmodelPreviewSetup>();

        // Save
        var dir = System.IO.Path.GetDirectoryName(ScenePath);
        if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir!);
        EditorSceneManager.SaveScene(scene, ScenePath);

        AssetDatabase.Refresh();
        Debug.Log($"[ViewmodelPreview] Scene created at {ScenePath}");
    }
}
