using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ViewmodelPreviewSceneCreator
{
    private const string ScenePath = "Assets/Collaborators/6aTowKa/Scenes/ViewmodelPreview.unity";

    [MenuItem("Tools/Wherever I Am/Open Viewmodel Preview Scene")]
    public static void OpenOrCreatePreviewScene()
    {
        if (!System.IO.File.Exists(ScenePath))
            CreateScene();
        else
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
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
