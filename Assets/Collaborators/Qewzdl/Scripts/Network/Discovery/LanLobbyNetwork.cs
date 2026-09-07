using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

// The two numbers both halves of discovery have to agree about, read the same
// way by both.
//
// Taken off the running network rather than out of the configuration asset,
// because that is what a host is actually using: the protocol version is
// whatever was applied to the config when the session was built, and the port
// is whatever the transport was told to listen on. An advert quoting an asset
// while the host listens somewhere else is an advert that leads nowhere.
//
// Read through one pair of properties rather than at each end, so that the two
// cannot disagree about how to read them - which would be the same bug as
// disagreeing about the numbers, and harder to see.
internal static class LanLobbyNetwork
{
    // What the game falls back on with no network in the picture. It only has
    // to be the same on both sides: a browser and a beacon that both answer
    // nought still find each other, and the moment either has a real session
    // they both take their number from the same place.
    private const int UnknownProtocol = 0;
    private const ushort DefaultPort = 7777;

    internal static int ProtocolVersion
    {
        get
        {
            NetworkManager manager = NetworkManager.Singleton;

            return manager != null && manager.NetworkConfig != null
                ? manager.NetworkConfig.ProtocolVersion
                : UnknownProtocol;
        }
    }

    internal static ushort GamePort
    {
        get
        {
            NetworkManager manager = NetworkManager.Singleton;
            UnityTransport transport = manager != null
                ? manager.NetworkConfig?.NetworkTransport as UnityTransport
                : null;

            ushort port = transport != null ? transport.ConnectionData.Port : (ushort)0;

            return port != 0 ? port : DefaultPort;
        }
    }
}
