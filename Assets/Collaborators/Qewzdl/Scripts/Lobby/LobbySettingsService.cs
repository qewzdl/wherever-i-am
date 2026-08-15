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

    public void SetGameMode(int gameModeId)
    {
        if (!CanChangeSettings())
            return;

        if (!IsValidGameModeId(gameModeId))
            return;

        LobbySettingsData settings = lobbyState.Settings.Value;
        settings.GameModeId = gameModeId;
        lobbyState.Settings.Value = settings;
    }

    public void SetMap(int mapId)
    {
        if (!CanChangeSettings())
            return;

        if (!IsValidMapId(mapId))
            return;

        LobbySettingsData settings = lobbyState.Settings.Value;
        settings.MapId = mapId;
        lobbyState.Settings.Value = settings;
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

    public void SetDifficulty(int difficultyId)
    {
        if (!CanChangeSettings())
            return;

        if (!IsValidDifficultyId(difficultyId))
            return;

        LobbySettingsData settings = lobbyState.Settings.Value;
        settings.DifficultyId = difficultyId;
        lobbyState.Settings.Value = settings;
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
