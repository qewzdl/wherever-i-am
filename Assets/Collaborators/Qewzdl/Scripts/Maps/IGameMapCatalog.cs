public interface IGameMapCatalog
{
    int DefaultMapId { get; }
    int Count { get; }

    bool IsValidMapId(int mapId);
    bool TryGetMap(int mapId, out GameMapDefinition map);
    bool TryGetMap(string sceneName, string scenePath, out GameMapDefinition map);
    bool IsValid(out string error);
}
