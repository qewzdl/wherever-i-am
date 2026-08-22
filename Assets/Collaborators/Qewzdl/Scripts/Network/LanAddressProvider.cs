using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

// The address a host has to read out loud.
//
// Joining is done by typing an IP into the main menu, which means somebody has
// to know it, and until now nothing in the game did. The host's own screen said
// nothing about it, so the only way through was a command prompt - for a game
// whose lobby is otherwise a single button.
//
// Asked of the adapters rather than of DNS. Dns.GetHostEntry on the local name
// answers with whatever the machine happens to resolve to, which on a box with
// a VPN or a virtual switch is routinely the wrong one of five; the adapter
// list can at least be filtered down to the ones that are up and are not
// loopback or tunnels.
public static class LanAddressProvider
{
    private const string UnknownAddress = "";

    // Looked up once. It can change while a lobby is open - a cable comes out,
    // a laptop moves to another network - but the host reads it out in the
    // first ten seconds and nothing here is worth a poll.
    private static string cached;

    public static string Get()
    {
        return cached ??= Resolve();
    }

    private static string Resolve()
    {
        // Gateway first. An interface with one is the one that reaches other
        // machines, which is the entire question being asked - the rest are
        // virtual switches, host-only adapters and VPN tunnels that answer
        // with an address nobody else can route to.
        string withGateway = FindAddress(requireGateway: true);

        return string.IsNullOrEmpty(withGateway)
            ? FindAddress(requireGateway: false)
            : withGateway;
    }

    private static string FindAddress(bool requireGateway)
    {
        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up)
                continue;

            if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            IPInterfaceProperties properties = adapter.GetIPProperties();

            if (requireGateway && properties.GatewayAddresses.Count == 0)
                continue;

            foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                // 169.254.x.x is what a machine gives itself when nothing
                // answered. Reading one of those out loud wastes the two
                // minutes it takes both people to find out it was never going
                // to work.
                if (IPAddress.IsLoopback(unicast.Address) || IsSelfAssigned(unicast.Address))
                    continue;

                return unicast.Address.ToString();
            }
        }

        return UnknownAddress;
    }

    private static bool IsSelfAssigned(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();

        return bytes[0] == 169 && bytes[1] == 254;
    }
}
