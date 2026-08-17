using UnityEngine;

// The name this player chose, kept between runs. Cleaned on the way in as well
// as on the way out: the host does not trust it either way, but there is no
// reason to store markup nobody will ever be shown.
public static class PlayerNameProvider
{
    private const string PlayerNameKey = "wia.playerName";

    public static string Get()
    {
        return NetworkConnectionPayloadCodec.NormalizePlayerName(
            PlayerPrefs.GetString(PlayerNameKey, string.Empty));
    }

    public static void Set(string playerName)
    {
        string normalized =
            NetworkConnectionPayloadCodec.NormalizePlayerName(playerName);

        PlayerPrefs.SetString(PlayerNameKey, normalized);
        PlayerPrefs.Save();
    }
}
