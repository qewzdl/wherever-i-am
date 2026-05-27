using UnityEngine;

[CreateAssetMenu(fileName = "LobbyConfig", menuName = "Wherever I Am/Lobby/Lobby Config")]
public class LobbyConfig : ScriptableObject
{
    [Header("Players")]
    [SerializeField] private int minPlayersToStart = 1;
    [SerializeField] private int maxPlayers = 4;
    [SerializeField] private bool requireAllPlayersReady = true;

    [Header("Allowed Selections")]
    [SerializeField] private int[] gameModeIds = { 0 };
    [SerializeField] private int[] mapIds = { 0 };

    public int MinPlayersToStart => minPlayersToStart;
    public int MaxPlayers => maxPlayers;
    public bool RequireAllPlayersReady => requireAllPlayersReady;

    public int DefaultGameModeId => GetFirstOrDefault(gameModeIds);
    public int DefaultMapId => GetFirstOrDefault(mapIds);

    public bool IsValidGameModeId(int gameModeId)
    {
        return ContainsId(gameModeIds, gameModeId);
    }

    public bool IsValidMapId(int mapId)
    {
        return ContainsId(mapIds, mapId);
    }

    private static int GetFirstOrDefault(int[] ids)
    {
        return ids != null && ids.Length > 0
            ? ids[0]
            : 0;
    }

    private static bool ContainsId(int[] ids, int id)
    {
        if (ids == null || ids.Length == 0)
            return id == 0;

        for (int i = 0; i < ids.Length; i++)
        {
            if (ids[i] == id)
                return true;
        }

        return false;
    }

    private void OnValidate()
    {
        maxPlayers = Mathf.Max(1, maxPlayers);
        minPlayersToStart = Mathf.Clamp(minPlayersToStart, 1, maxPlayers);
    }
}
