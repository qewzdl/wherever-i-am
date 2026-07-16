using UnityEngine;

[CreateAssetMenu(menuName = "Wherever I Am/Project Settings", fileName = "ProjectSettings")]
public sealed class ProjectSettings : ScriptableObject
{
    [Header("Startup")]
    [SerializeField] private ProjectSceneKind bootstrapScene;
    [SerializeField] private ProjectSceneKind defaultStartupScene;

    [Header("Scenes")]
    [SerializeField] private ProjectSceneDefinition[] scenes;

    public ProjectSceneKind BootstrapScene => bootstrapScene;
    public ProjectSceneKind DefaultStartupScene => defaultStartupScene;

    public string GetSceneName(ProjectSceneKind kind)
    {
        return TryGetScene(kind, out ProjectSceneDefinition scene)
            ? scene.SceneName
            : string.Empty;
    }

    public string GetScenePath(ProjectSceneKind kind)
    {
        return TryGetScene(kind, out ProjectSceneDefinition scene)
            ? scene.ScenePath
            : string.Empty;
    }

    public bool TryGetScene(ProjectSceneKind kind, out ProjectSceneDefinition scene)
    {
        if (scenes != null)
        {
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].Kind != kind)
                    continue;

                scene = scenes[i];
                return true;
            }
        }

        scene = default;
        return false;
    }

    public bool TryGetScene(string sceneName, string scenePath, out ProjectSceneDefinition scene)
    {
        if (scenes != null)
        {
            for (int i = 0; i < scenes.Length; i++)
            {
                ProjectSceneDefinition candidate = scenes[i];

                if (candidate.Matches(sceneName, scenePath))
                {
                    scene = candidate;
                    return true;
                }
            }
        }

        scene = default;
        return false;
    }

    public ProjectSceneKind GetSceneKind(string sceneName, string scenePath)
    {
        return TryGetScene(sceneName, scenePath, out ProjectSceneDefinition scene)
            ? scene.Kind
            : ProjectSceneKind.Unknown;
    }
}
