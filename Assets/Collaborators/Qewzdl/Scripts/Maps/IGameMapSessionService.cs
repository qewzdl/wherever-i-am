using System;
using UnityEngine;

public interface IGameMapSessionService
{
    IGameMapCatalog Catalog { get; }
    GameMapDefinition SelectedMap { get; }
    GameMapDefinition ActiveMap { get; }
    GameMapRoot ActiveMapRoot { get; }
    bool IsReadyForMatch { get; }

    event Action MapReady;

    bool SelectMap(int mapId);
    bool TryGetPlayerSpawn(ulong clientId, out Vector3 position, out Quaternion rotation);
}
