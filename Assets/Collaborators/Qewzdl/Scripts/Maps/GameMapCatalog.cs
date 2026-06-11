using System;
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
    public GameMapDefinition GetMapAt(int index)
    {
        if (maps == null || index < 0 || index >= maps.Length)
            return null;

        return maps[index];
    }

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
        Array.Sort(maps, CompareMaps);

        if (currentCount == 0)
            defaultMapId = map.MapId;

        return true;
    }

    public int GetNextAvailableMapIdEditor()
    {
        int candidate = 0;

        while (TryGetMap(candidate, out _))
            candidate++;

        return candidate;
    }

    public bool SetDefaultMapEditor(int mapId)
    {
        if (!TryGetMap(mapId, out _))
            return false;

        defaultMapId = mapId;
        return true;
    }

    public bool RemoveMapEditor(int mapId)
    {
        if (maps == null || maps.Length == 0)
            return false;

        for (int i = 0; i < maps.Length; i++)
        {
            if (maps[i] != null && maps[i].MapId == mapId)
                return RemoveMapAtEditor(i);
        }

        return false;
    }

    public bool RemoveMapAtEditor(int index)
    {
        if (maps == null || index < 0 || index >= maps.Length)
            return false;

        int removedMapId = maps[index] == null ? -1 : maps[index].MapId;
        GameMapDefinition[] nextMaps = new GameMapDefinition[maps.Length - 1];

        for (int sourceIndex = 0, targetIndex = 0; sourceIndex < maps.Length; sourceIndex++)
        {
            if (sourceIndex == index)
                continue;

            nextMaps[targetIndex] = maps[sourceIndex];
            targetIndex++;
        }

        maps = nextMaps;

        if (maps.Length > 0 && (defaultMapId == removedMapId || !TryGetMap(defaultMapId, out _)))
        {
            for (int i = 0; i < maps.Length; i++)
            {
                if (maps[i] == null)
                    continue;

                defaultMapId = maps[i].MapId;
                break;
            }
        }

        return true;
    }

    private static int CompareMaps(GameMapDefinition left, GameMapDefinition right)
    {
        if (ReferenceEquals(left, right))
            return 0;

        if (left == null)
            return 1;

        if (right == null)
            return -1;

        return left.MapId.CompareTo(right.MapId);
    }

    private void OnValidate()
    {
        defaultMapId = Mathf.Max(0, defaultMapId);
    }
#endif
}
