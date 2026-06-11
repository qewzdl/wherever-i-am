using System;
using UnityEngine;

[CreateAssetMenu(
    fileName = "GameMapDefinition",
    menuName = "Wherever I Am/Maps/Game Map Definition")]
public sealed class GameMapDefinition : ScriptableObject
{
    [SerializeField] [Min(0)] private int mapId;
    [SerializeField] private string displayName;
    [SerializeField] private string sceneName;
    [SerializeField] private string scenePath;
    [SerializeField] private ObjectiveSequenceDefinition objectiveSequenceOverride;

    public int MapId => mapId;
    public string DisplayName => displayName;
    public string SceneName => sceneName;
    public string ScenePath => scenePath;
    public ObjectiveSequenceDefinition ObjectiveSequenceOverride => objectiveSequenceOverride;

    public bool IsConfigured(out string error)
    {
        if (mapId < 0)
        {
            error = $"{nameof(GameMapDefinition)} '{name}' has a negative map id.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            error = $"{nameof(GameMapDefinition)} '{name}' has no display name.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            error = $"{nameof(GameMapDefinition)} '{name}' has no scene name.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(scenePath))
        {
            error = $"{nameof(GameMapDefinition)} '{name}' has no scene path.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public bool MatchesScene(string candidateName, string candidatePath)
    {
        return NamesEqual(candidateName, sceneName) ||
               PathsEqual(candidatePath, scenePath);
    }

    private static bool NamesEqual(string left, string right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
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

#if UNITY_EDITOR
    public void ConfigureEditor(
        int id,
        string mapDisplayName,
        string mapSceneName,
        string mapScenePath)
    {
        mapId = Mathf.Max(0, id);
        displayName = mapDisplayName;
        sceneName = mapSceneName;
        scenePath = NormalizePath(mapScenePath);
    }

    private void OnValidate()
    {
        mapId = Mathf.Max(0, mapId);
    }
#endif
}
