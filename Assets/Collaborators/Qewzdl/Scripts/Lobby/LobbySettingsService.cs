using UnityEngine;

public class LobbySettingsService
{
    private readonly LobbyState lobbyState;
    private readonly LobbyConfig lobbyConfig;
    private readonly IGameMapSessionService mapService;
    private readonly INetworkSessionAdmissionService admissionService;

    public LobbySettingsService(
        LobbyState lobbyState,
        LobbyConfig lobbyConfig,
        IGameMapSessionService mapService = null,
        INetworkSessionAdmissionService admissionService = null)
    {
        this.lobbyState = lobbyState;
        this.lobbyConfig = lobbyConfig;
        this.mapService = mapService;
        this.admissionService = admissionService;
    }

    // The room as the host last left it, falling back to the config for a room
    // nobody has set up yet.
    //
    // LobbyState is an in-scene object, so it dies when a match loads and comes
    // back with its NetworkVariables at their declared defaults. Seeding it
    // from the config alone was therefore not a starting point but an erasure:
    // every match ended by putting the difficulty back to default and shutting
    // a door the host had opened - and a shut door stops the beacon, so the
    // room also dropped out of everybody's browser.
    //
    // Nothing new remembers this. Both choices already outlive the Lobby scene
    // inside the services that act on them: the map service is handed the
    // difficulty when the match starts, and the admission service has been
    // holding the door the whole time because it is the thing that turns
    // people away. They are read back now instead of being overwritten.
    //
    // The rest of the struct is the config's to state - the player counts, the
    // readiness rule - and re-reading those is correct rather than lossy. They
    // are the same numbers every time.
    public void Initialize()
    {
        if (!HasLobbyState())
            return;

        LobbySettingsData settings = LobbySettingsData.FromConfig(lobbyConfig);

        if (mapService != null)
        {
            if (mapService.SelectedMap != null &&
                IsValidMapId(mapService.SelectedMap.MapId))
            {
                settings.MapId = mapService.SelectedMap.MapId;
            }

            if (IsValidDifficultyId(mapService.SelectedDifficultyId))
                settings.DifficultyId = mapService.SelectedDifficultyId;
        }

        if (admissionService != null)
            settings.IsPublic = admissionService.IsAcceptingNewPlayers;

        lobbyState.Settings.Value = settings;
        lobbyState.Phase.Value = LobbyPhase.Open;
    }

    // Returns whether anything moved. The caller clears everybody's ready when
    // it did, and re-picking the option already selected has to not count -
    // otherwise a host tapping through a list to look at the descriptions
    // stands the whole room down.
    public bool SetGameMode(int gameModeId)
    {
        if (!CanChangeSettings())
            return false;

        if (!IsValidGameModeId(gameModeId))
            return false;

        LobbySettingsData settings = lobbyState.Settings.Value;

        if (settings.GameModeId == gameModeId)
            return false;

        settings.GameModeId = gameModeId;
        lobbyState.Settings.Value = settings;
        return true;
    }

    public bool SetMap(int mapId)
    {
        if (!CanChangeSettings())
            return false;

        if (!IsValidMapId(mapId))
            return false;

        LobbySettingsData settings = lobbyState.Settings.Value;

        if (settings.MapId == mapId)
            return false;

        settings.MapId = mapId;
        lobbyState.Settings.Value = settings;
        return true;
    }

    public void SetLobbyPublic(bool isPublic)
    {
        if (!CanChangeSettings())
            return;

        LobbySettingsData settings = lobbyState.Settings.Value;

        if (settings.IsPublic == isPublic)
            return;

        settings.IsPublic = isPublic;
        lobbyState.Settings.Value = settings;
    }

    public bool SetDifficulty(int difficultyId)
    {
        if (!CanChangeSettings())
            return false;

        if (!IsValidDifficultyId(difficultyId))
            return false;

        LobbySettingsData settings = lobbyState.Settings.Value;

        if (settings.DifficultyId == difficultyId)
            return false;

        settings.DifficultyId = difficultyId;
        lobbyState.Settings.Value = settings;
        return true;
    }

    private bool IsValidDifficultyId(int difficultyId)
    {
        if (lobbyConfig != null && lobbyConfig.IsValidDifficultyId(difficultyId))
            return true;

        // Quiet about the one value that means nothing has been chosen yet. A
        // session that has never started a match has no difficulty to carry
        // over, and saying so every time a lobby opens is noise.
        if (difficultyId != GameMapService.NoDifficultySelected)
            Debug.LogWarning($"Rejected invalid lobby difficulty id: {difficultyId}.");

        return false;
    }

    private bool IsValidGameModeId(int gameModeId)
    {
        if (lobbyConfig != null && lobbyConfig.IsValidGameModeId(gameModeId))
            return true;

        Debug.LogWarning($"Rejected invalid lobby game mode id: {gameModeId}.");
        return false;
    }

    private bool IsValidMapId(int mapId)
    {
        if (lobbyConfig != null && lobbyConfig.IsValidMapId(mapId))
            return true;

        Debug.LogWarning($"Rejected invalid lobby map id: {mapId}.");
        return false;
    }

    private bool CanChangeSettings()
    {
        if (!HasLobbyState())
            return false;

        if (lobbyState.Phase.Value != LobbyPhase.Open)
        {
            Debug.LogWarning($"Lobby settings cannot be changed while lobby phase is {lobbyState.Phase.Value}.");
            return false;
        }

        return true;
    }

    private bool HasLobbyState()
    {
        if (lobbyState != null)
            return true;

        Debug.LogError("LobbyState is missing.");
        return false;
    }
}
