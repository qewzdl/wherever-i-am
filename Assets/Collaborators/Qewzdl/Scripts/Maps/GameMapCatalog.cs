using UnityEngine;

[CreateAssetMenu(
    fileName = "GameMapCatalog",
    menuName = "Wherever I Am/Maps/Game Map Catalog")]
public sealed class GameMapCatalog : ScriptableObject
{
    [SerializeField] [Min(0)] private int defaultMapId;
    [SerializeField] private GameMapDefinition[] maps;

    public int DefaultMapId => defaultMapId;
    public int Count => maps == null ? 0 : maps.Length;

    public bool IsValidMapId(int mapId)
    {
        return TryGetMap(mapId, out _);
    }

    public bool TryGetMap(int mapId, out GameMapDefinition map)
    {
        if (maps != null)
        {
            for (int i = 0; i < maps.Length; i++)
            {
                GameMapDefinition candidate = maps[i];

                if (candidate != null && candidate.MapId == mapId)
                {
                    map = candidate;
                    return true;
                }
            }
        }

        map = null;
        return false;
    }

    public bool TryGetMap(string sceneName, string scenePath, out GameMapDefinition map)
    {
        if (maps != null)
        {
            for (int i = 0; i < maps.Length; i++)
            {
                GameMapDefinition candidate = maps[i];

                if (candidate != null && candidate.MatchesScene(sceneName, scenePath))
                {
                    map = candidate;
                    return true;
                }
            }
        }

        map = null;
        return false;
    }

    public bool IsValid(out string error)
    {
        if (maps == null || maps.Length == 0)
        {
            error = $"{nameof(GameMapCatalog)} '{name}' has no maps.";
            return false;
        }

        for (int i = 0; i < maps.Length; i++)
        {
            GameMapDefinition map = maps[i];

            if (map == null)
            {
                error = $"{nameof(GameMapCatalog)} '{name}' has a null map at index {i}.";
                return false;
            }

            if (!map.IsConfigured(out error))
                return false;

            for (int j = i + 1; j < maps.Length; j++)
            {
                GameMapDefinition other = maps[j];

                if (other == null)
                    continue;

                if (other.MapId == map.MapId)
                {
                    error = $"{nameof(GameMapCatalog)} '{name}' has duplicate map id {map.MapId}.";
                    return false;
                }

                if (other.MatchesScene(map.SceneName, map.ScenePath))
                {
                    error = $"{nameof(GameMapCatalog)} '{name}' registers scene '{map.SceneName}' more than once.";
                    return false;
                }
            }
        }

        if (!TryGetMap(defaultMapId, out _))
        {
            error = $"{nameof(GameMapCatalog)} '{name}' has no map for default id {defaultMapId}.";
            return false;
        }

        error = string.Empty;
        return true;
    }

#if UNITY_EDITOR
    public bool AddMapEditor(GameMapDefinition map)
    {
        if (map == null || TryGetMap(map.MapId, out _))
            return false;

        int currentCount = maps == null ? 0 : maps.Length;
        GameMapDefinition[] nextMaps = new GameMapDefinition[currentCount + 1];

        for (int i = 0; i < currentCount; i++)
            nextMaps[i] = maps[i];

        nextMaps[currentCount] = map;
        maps = nextMaps;

        if (currentCount == 0)
            defaultMapId = map.MapId;

        return true;
    }

    private void OnValidate()
    {
        defaultMapId = Mathf.Max(0, defaultMapId);
    }
#endif
}
