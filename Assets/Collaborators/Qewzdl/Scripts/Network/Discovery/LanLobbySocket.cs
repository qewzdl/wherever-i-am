using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

// The one socket both halves of discovery use, and the two awkward facts about
// it kept in one place.
//
// The first is that a host and a browser have to share a port on the same
// machine. Two people testing this game on one box is the normal case, not the
// exotic one - that is what the virtual players are for - and without
// ReuseAddress the second process to start simply fails to bind and discovery
// looks broken on exactly the setup it is being tested on.
//
// The second is that reading a socket must not block a frame. Nothing here
// waits: Available is asked, and only what has already arrived is read. No
// thread, nothing to marshal back, and a frame costs whatever is actually in
// the buffer.
internal sealed class LanLobbySocket : IDisposable
{
    // One above the port the game itself uses. Not configurable on purpose -
    // two builds that disagree about it cannot see each other, which is a
    // worse failure than one that cannot be tuned.
    internal const int DiscoveryPort = 7778;

    private UdpClient client;
    private readonly IPEndPoint broadcast = new(IPAddress.Broadcast, DiscoveryPort);

    private IPEndPoint sender = new(IPAddress.Any, 0);

    internal bool IsOpen => client != null;

    internal LanLobbySocket()
    {
        try
        {
            client = new UdpClient();
            client.Client.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true);

            client.ExclusiveAddressUse = false;
            client.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            client.EnableBroadcast = true;
        }
        catch (SocketException exception)
        {
            // A port somebody else owns, or a machine with no network at all.
            // Discovery is the one part of this game that is allowed to be
            // absent: there is still an address field to type into.
            Debug.LogWarning($"Lobby discovery could not open its socket: {exception.Message}");
            Close();
        }
    }

    internal void Broadcast(string message)
    {
        Send(message, broadcast);
    }

    internal void Send(string message, IPEndPoint target)
    {
        if (client == null || string.IsNullOrEmpty(message) || target == null)
            return;

        try
        {
            byte[] payload = Encoding.UTF8.GetBytes(message);
            client.Send(payload, payload.Length, target);
        }
        catch (SocketException)
        {
            // A network that went away underneath us. The next beacon tries
            // again; there is nothing useful to say about one lost datagram.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    // Whatever has already arrived, and nothing more. False when the buffer is
    // empty, which is almost every frame.
    internal bool TryReceive(out string message, out IPEndPoint from)
    {
        message = null;
        from = null;

        if (client == null)
            return false;

        try
        {
            if (client.Available <= 0)
                return false;

            byte[] payload = client.Receive(ref sender);
            message = Encoding.UTF8.GetString(payload);
            from = sender;
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        Close();
    }

    // Nulled as well as closed, so IsOpen tells the truth after a bind that
    // failed: the client is made before it is bound, and a socket that never
    // reached a port is not a socket anybody should be sending on.
    private void Close()
    {
        try
        {
            client?.Close();
        }
        catch (SocketException)
        {
        }

        client = null;
    }
}
