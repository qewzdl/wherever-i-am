using UnityEngine;

// The address this player last joined, kept between runs, the way the chosen
// name is. Testing over a LAN means typing the same host address on every
// launch, and a menu that forgets it makes the player do the machine's work.
//
// Trimmed on the way in and on the way out: an address arrives by copy and
// paste more often than by typing, and a trailing space is a failed connection
// with nothing on screen to explain it.
public static class JoinAddressProvider
{
    private const string JoinAddressKey = "wia.joinAddress";

    public static string Get()
    {
        return Normalize(PlayerPrefs.GetString(JoinAddressKey, string.Empty));
    }

    public static void Set(string address)
    {
        PlayerPrefs.SetString(JoinAddressKey, Normalize(address));
        PlayerPrefs.Save();
    }

    public static string Normalize(string address)
    {
        return string.IsNullOrWhiteSpace(address) ? string.Empty : address.Trim();
    }
}
