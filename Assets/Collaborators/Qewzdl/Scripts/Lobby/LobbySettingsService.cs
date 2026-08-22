using UnityEngine;

public class LobbySettingsService
{
    private readonly LobbyState lobbyState;
    private readonly LobbyConfig lobbyConfig;

    public LobbySettingsService(LobbyState lobbyState, LobbyConfig lobbyConfig)
    {
        this.lobbyState = lobbyState;
        this.lobbyConfig = lobbyConfig;
    }

    public void InitializeFromConfig()
    {
        if (!HasLobbyState())
            return;

        lobbyState.Settings.Value = LobbySettingsData.FromConfig(lobbyConfig);
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
