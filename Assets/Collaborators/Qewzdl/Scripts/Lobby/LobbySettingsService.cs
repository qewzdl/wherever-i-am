using UnityEngine;

public class LobbySettingsService
{
    private readonly LobbyState lobbyState;

    public LobbySettingsService(LobbyState lobbyState)
    {
        this.lobbyState = lobbyState;
    }

    public void InitializeFromConfig(LobbyConfig lobbyConfig)
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

        LobbySettingsData settings = lobbyState.Settings.Value;
        settings.GameModeId = gameModeId;
        lobbyState.Settings.Value = settings;
    }

    public void SetMap(int mapId)
    {
        if (!CanChangeSettings())
            return;

        LobbySettingsData settings = lobbyState.Settings.Value;
        settings.MapId = mapId;
        lobbyState.Settings.Value = settings;
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