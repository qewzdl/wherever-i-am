using System;
using UnityEngine;

[Serializable]
public struct ProjectSceneDefinition
{
    [SerializeField] private ProjectSceneKind kind;
    [SerializeField] private string sceneName;
    [SerializeField] private string scenePath;
    [SerializeField] private GameState state;

    public ProjectSceneDefinition(
        ProjectSceneKind kind,
        string sceneName,
        string scenePath,
        GameState state)
    {
        this.kind = kind;
        this.sceneName = sceneName;
        this.scenePath = scenePath;
        this.state = state;
    }

    public ProjectSceneKind Kind => kind;
    public string SceneName => sceneName;
    public string ScenePath => scenePath;
    public GameState State => state;

    public bool Matches(string candidateName, string candidatePath)
    {
        return SceneNameEquals(candidateName, sceneName) ||
               ScenePathEquals(candidatePath, scenePath);
    }

    private static bool SceneNameEquals(string left, string right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ScenePathEquals(string left, string right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/');
    }
}
