using System.Collections.Generic;
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
//
// It answers with all of them now, best first, rather than with one. A machine
// running Radmin or Hamachi has two addresses that both work and reach
// different people, and picking for the player was guessing which friend they
// meant. The order is still an opinion - the local network before the overlay
// network - so anything that wants one answer can take the first and be right
// as often as this file used to be.
public static class LanAddressProvider
{
    // An address and the name of the road it is on. The label is what makes a
    // list of four numbers choosable: 26.x and 192.168.x look equally plausible
    // to somebody who has never had to know, and only one of them reaches the
    // friend sitting in the same building.
    public readonly struct Option
    {
        public readonly string Address;
        public readonly string AdapterName;
        public readonly bool IsOverlay;

        public Option(string address, string adapterName, bool isOverlay)
        {
            Address = address;
            AdapterName = adapterName;
            IsOverlay = isOverlay;
        }

        // "LAN - Ethernet", "VPN - Radmin VPN". The kind first, because it is
        // the part being chosen between; the adapter name after it, because a
        // machine with two of either kind needs telling which is which.
        public string Label => $"{(IsOverlay ? "VPN" : "LAN")} - {AdapterName}";
    }

    // Words that show up in the name of an adapter that is not a network so
    // much as a way of pretending to be on someone else's. Matched loosely and
    // on purpose: being wrong here costs a label, and the address still works.
    private static readonly string[] OverlayMarkers =
    {
        "radmin",
        "hamachi",
        "zerotier",
        "tailscale",
        "wireguard",
        "openvpn",
        "vpn",
        "virtual",
        "vmware",
        "virtualbox",
        "hyper-v",
        "tap-",
        "tun",
    };

    // Looked up once. It can change while a lobby is open - a cable comes out,
    // a laptop moves to another network - but the host reads it out in the
    // first ten seconds and nothing here is worth a poll.
    private static IReadOnlyList<Option> cached;

    public static IReadOnlyList<Option> GetAll()
    {
        return cached ??= Resolve();
    }

    // The one address to show when there is only room for one.
    public static string Get()
    {
        IReadOnlyList<Option> options = GetAll();

        return options.Count > 0 ? options[0].Address : string.Empty;
    }

    private static IReadOnlyList<Option> Resolve()
    {
        List<Option> local = new List<Option>();
        List<Option> localWithoutGateway = new List<Option>();
        List<Option> overlay = new List<Option>();

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

            bool isOverlay = IsOverlay(adapter);

            // A gateway is what says an interface reaches machines it was not
            // set up to reach. It used to be a filter, which threw away the
            // overlay networks entirely - and an overlay network is exactly how
            // two people on different broadband connections play this game.
            // It sorts now instead of excluding.
            bool hasGateway = properties.GatewayAddresses.Count > 0;

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

                Option option = new Option(
                    unicast.Address.ToString(),
                    Describe(adapter),
                    isOverlay);

                if (isOverlay)
                    overlay.Add(option);
                else if (hasGateway)
                    local.Add(option);
                else
                    localWithoutGateway.Add(option);
            }
        }

        // Real network first, then a real one that cannot route, then the
        // overlays. The bug this ordering exists for: a host with Radmin
        // installed was being handed 26.x to read out to the friend sitting on
        // the same Wi-Fi, and it is the last address either of them would have
        // thought to doubt.
        local.AddRange(localWithoutGateway);
        local.AddRange(overlay);

        return local;
    }

    private static bool IsOverlay(NetworkInterface adapter)
    {
        return adapter.NetworkInterfaceType == NetworkInterfaceType.Ppp ||
               LooksLikeOverlay($"{adapter.Name} {adapter.Description}");
    }

    // Split out from the adapter so the one piece of judgement in this file can
    // be checked without a machine that happens to have Radmin installed.
    public static bool LooksLikeOverlay(string adapterName)
    {
        if (string.IsNullOrEmpty(adapterName))
            return false;

        string name = adapterName.ToLowerInvariant();

        foreach (string marker in OverlayMarkers)
        {
            if (name.Contains(marker))
                return true;
        }

        return false;
    }

    // The adapter's own name, which on Windows is what the player renamed it to
    // in their network settings - "Ethernet", "Wi-Fi", "Radmin VPN". The
    // description is the hardware, and nobody chooses a network by chipset.
    private static string Describe(NetworkInterface adapter)
    {
        return string.IsNullOrWhiteSpace(adapter.Name)
            ? adapter.Description
            : adapter.Name;
    }

    private static bool IsSelfAssigned(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();

        return bytes[0] == 169 && bytes[1] == 254;
    }
}
