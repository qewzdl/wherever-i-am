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
    }

    public void SetGameMode(int gameModeId)
    {
        if (!HasLobbyState())
            return;

        LobbySettingsData settings = lobbyState.Settings.Value;
        settings.GameModeId = gameModeId;
        lobbyState.Settings.Value = settings;
    }

    public void SetMap(int mapId)
    {
        if (!HasLobbyState())
            return;

        LobbySettingsData settings = lobbyState.Settings.Value;
        settings.MapId = mapId;
        lobbyState.Settings.Value = settings;
    }

    private bool HasLobbyState()
    {
        if (lobbyState != null)
            return true;

        Debug.LogError("LobbyState is missing.");
        return false;
    }
}