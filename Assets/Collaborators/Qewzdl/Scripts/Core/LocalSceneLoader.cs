using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

[DisallowMultipleComponent]
public sealed class LocalSceneLoader : MonoBehaviour
{
    [SerializeField] private ProjectContext projectContext;

    public void Construct(ProjectContext context)
    {
        projectContext = context;
    }

    public bool Load(ProjectSceneKind sceneKind)
    {
        if (!HasProjectContext())
            return false;

        if (!projectContext.TryGetScene(sceneKind, out ProjectSceneDefinition scene))
        {
            Debug.LogError($"Local scene is not configured for {sceneKind}.", this);
            return false;
        }

        return Load(scene);
    }

    public bool Load(ProjectSceneDefinition scene)
    {
        if (!HasProjectContext())
            return false;

        if (string.IsNullOrWhiteSpace(scene.SceneName) && string.IsNullOrWhiteSpace(scene.ScenePath))
        {
            Debug.LogError($"Scene '{scene.Kind}' has no configured name or path.", this);
            return false;
        }

        LoadConfiguredScene(scene.SceneName, scene.ScenePath);
        return true;
    }

    private bool HasProjectContext()
    {
        if (projectContext != null)
            return true;

        Debug.LogError($"{nameof(LocalSceneLoader)} is missing {nameof(ProjectContext)}.", this);
        return false;
    }

    private static void LoadConfiguredScene(string sceneName, string scenePath)
    {
#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(scenePath))
        {
            EditorSceneManager.LoadSceneInPlayMode(
                scenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
            return;
        }
#endif

        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }
}