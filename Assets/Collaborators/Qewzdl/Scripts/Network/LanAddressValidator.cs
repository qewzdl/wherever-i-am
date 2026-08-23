using System.Net;
using System.Net.Sockets;

// One definition of an address the LAN transport can actually use. The main
// menu asks before enabling Connect, and the connection strategy asks again at
// the trust boundary before it touches Unity Transport.
public static class LanAddressValidator
{
    public static bool TryNormalize(string value, out string address)
    {
        address = JoinAddressProvider.Normalize(value);

        if (string.IsNullOrEmpty(address))
            return false;

        string[] parts = address.Split('.');

        if (parts.Length != 4)
            return false;

        for (int i = 0; i < parts.Length; i++)
        {
            if (!byte.TryParse(parts[i], out _))
                return false;
        }

        if (!IPAddress.TryParse(address, out IPAddress parsed))
            return false;

        return parsed.AddressFamily == AddressFamily.InterNetwork &&
               !IPAddress.Any.Equals(parsed) &&
               !IPAddress.Broadcast.Equals(parsed) &&
               !IPAddress.IPv6Any.Equals(parsed);
    }
}
