public interface IGameMapCatalog
{
    int DefaultMapId { get; }
    int Count { get; }

    // In the order the maps are played in - see MapProgress.
    GameMapDefinition GetMapAt(int index);

    bool IsValidMapId(int mapId);
    bool TryGetMap(int mapId, out GameMapDefinition map);
    bool TryGetMap(string sceneName, string scenePath, out GameMapDefinition map);
    bool IsValid(out string error);
}
