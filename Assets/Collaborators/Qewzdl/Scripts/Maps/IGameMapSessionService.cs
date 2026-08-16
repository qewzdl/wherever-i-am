using System;
using UnityEngine;

public interface IGameMapSessionService
{
    IGameMapCatalog Catalog { get; }
    GameMapDefinition SelectedMap { get; }
    GameMapDefinition ActiveMap { get; }
    GameMapRoot ActiveMapRoot { get; }
    bool IsReadyForMatch { get; }

    // What the host picked in the lobby, resolved on the server before the map
    // loads. Null until a difficulty is selected or defaulted.
    // ponytail: the match setup the lobby sends lives next to the map because
    // it arrives in the same StartGame call. A third setting earns its own
    // session contract.
    EnemyConfig SelectedEnemyConfig { get; }

    event Action MapReady;

    bool SelectMap(int mapId);
    bool SelectDifficulty(int difficultyId);
    bool TryGetPlayerSpawn(ulong clientId, out Vector3 position, out Quaternion rotation);
}
