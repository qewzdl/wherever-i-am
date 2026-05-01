using UnityEngine;

[CreateAssetMenu(menuName = "Wherever I Am/Lobby Config")]
public class LobbyConfig : ScriptableObject
{
    [Header("Players")]
    [SerializeField] private int minPlayersToStart = 1;
    [SerializeField] private int maxPlayers = 4;
    [SerializeField] private bool requireAllPlayersReady = true;

    public int MinPlayersToStart => minPlayersToStart;
    public int MaxPlayers => maxPlayers;
    public bool RequireAllPlayersReady => requireAllPlayersReady;
}