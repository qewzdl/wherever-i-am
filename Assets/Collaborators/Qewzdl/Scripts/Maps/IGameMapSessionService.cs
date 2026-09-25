using System;
using UnityEngine;

public interface IGameMapSessionService
{
    IGameMapCatalog Catalog { get; }
    GameMapDefinition SelectedMap { get; }
    GameMapDefinition ActiveMap { get; }
    GameMapRoot ActiveMapRoot { get; }
    bool IsReadyForMatch { get; }

    // The difficulty the host picked in the lobby, as the id every enemy looks
    // its own config up by; -1 until something is chosen or defaulted. Kept
    // here because the lobby has to ask for it back: its own copy dies with
    // the Lobby scene when a match loads, and this outlives the round trip.
    // ponytail: the match setup the lobby sends lives next to the map because
    // it arrives in the same StartGame call. A third setting earns its own
    // session contract.
    int SelectedDifficultyId { get; }

    event Action MapReady;

    bool SelectMap(int mapId);
    bool SelectDifficulty(int difficultyId);
    bool TryGetPlayerSpawn(ulong clientId, out Vector3 position, out Quaternion rotation);
}
