using System;
using System.Collections.Generic;
using System.Net;
using UnityEngine;

// The list of lobbies that have said they are open, kept current by the fact
// that they keep saying it.
//
// Nothing is polled and nothing is asked. A host broadcasts while its door is
// open, this hears it and the row appears; the host shuts its door or its
// machine goes away, the beacons stop, and the row ages out on its own a few
// seconds later. That is the whole of the live behaviour, and it needs no
// server to be live against - which is just as well, because this game has
// none.
//
// Refresh is still worth having and does two things worth doing: it forgets
// everything currently listed, so a stale row cannot outlive the truth by even
// the timeout, and it asks out loud, so hosts answer at once instead of the
// list filling in over the next second.
public sealed class LanLobbyDiscovery : IDisposable
{
    // Four missed beacons. Long enough that one lost datagram on a busy network
    // does not blink a room out of the list, short enough that a lobby which
    // has closed is gone before anybody tries to join it.
    private const float ForgetAfterSeconds = 4f;

    // And asked again, twice inside that window, for as long as the list is on
    // screen.
    //
    // Listening was not enough. A beacon is a broadcast addressed to nobody,
    // and the machine looking at this list is entitled to drop it: a firewall
    // opens a stateful hole for a datagram that answers one we sent and none
    // for one that arrives out of the blue, and a host with a VPN or a virtual
    // adapter puts 255.255.255.255 on whichever interface the routing table
    // liked best, which is often not the one the game is on.
    //
    // Either way the symptom is the same and it is the one that was reported:
    // the probe on opening gets a reply, so a room appears, and then nothing
    // arrives again and it ages out four seconds later. Asking is what the
    // reply is an answer to, so asking again is what keeps it coming.
    private const float ProbeSeconds = 2f;

    public readonly struct Entry
    {
        public readonly IPEndPoint EndPoint;
        public readonly LanLobbyAdvert Advert;
        public readonly float SeenAt;

        public Entry(IPEndPoint endPoint, LanLobbyAdvert advert, float seenAt)
        {
            EndPoint = endPoint;
            Advert = advert;
            SeenAt = seenAt;
        }

        public string Address => EndPoint != null ? EndPoint.Address.ToString() : string.Empty;
    }

    private readonly Dictionary<string, Entry> byAddress = new();
    private readonly List<Entry> ordered = new();
    private readonly List<string> expired = new();
    private readonly int protocolVersion;

    private LanLobbySocket socket;
    private float nextProbeAt;

    public event Action Changed;

    // In the order they were first heard, which is stable while nothing joins
    // or leaves - a list that reshuffles under the pointer is one nobody can
    // click.
    public IReadOnlyList<Entry> Lobbies => ordered;

    public bool IsListening => socket != null && socket.IsOpen;

    // Whether a room is still saying it is there. Asked by whoever is holding
    // on to an address the player picked: a choice outlives the row it was
    // made on otherwise, and points at a lobby that has gone quiet.
    public bool Knows(string address)
    {
        return !string.IsNullOrEmpty(address) && byAddress.ContainsKey(address);
    }

    public LanLobbyDiscovery(int protocolVersion)
    {
        this.protocolVersion = protocolVersion;
        socket = new LanLobbySocket();
    }

    // Emptied and asked again. The emptying is the half that matters: without
    // it, Refresh on a network where every host has gone quiet would leave the
    // old rows sitting there looking joinable until their timeout ran out.
    public void Refresh()
    {
        bool had = ordered.Count > 0;

        byAddress.Clear();
        ordered.Clear();

        Probe();

        if (had)
            Changed?.Invoke();
    }

    // Driven by whoever is showing the list, once a frame. Nothing here waits
    // on the network: only what has already arrived is read.
    public void Tick()
    {
        if (socket == null || !socket.IsOpen)
            return;

        if (Time.unscaledTime >= nextProbeAt)
            Probe();

        bool changed = false;

        while (socket.TryReceive(out string message, out IPEndPoint from))
        {
            if (!LanLobbyAdvert.TryParse(message, protocolVersion, out LanLobbyAdvert advert))
                continue;

            changed |= Remember(from, advert);
        }

        changed |= ForgetTheSilent();

        if (changed)
            Changed?.Invoke();
    }

    private void Probe()
    {
        nextProbeAt = Time.unscaledTime + ProbeSeconds;
        socket?.Broadcast(LanLobbyAdvert.ProbeMessage);
    }

    public void Dispose()
    {
        socket?.Dispose();
        socket = null;

        byAddress.Clear();
        ordered.Clear();
    }

    // Keyed by address rather than by endpoint: the port a beacon was sent from
    // is not the port the game is on, and one machine is one lobby.
    private bool Remember(IPEndPoint from, LanLobbyAdvert advert)
    {
        string address = from.Address.ToString();
        IPEndPoint game = new(from.Address, advert.port);
        Entry entry = new(game, advert, Time.unscaledTime);

        if (!byAddress.TryGetValue(address, out Entry known))
        {
            byAddress[address] = entry;
            ordered.Add(entry);
            return true;
        }

        byAddress[address] = entry;

        for (int i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Address != address)
                continue;

            ordered[i] = entry;
            break;
        }

        // A beacon that says the same thing as the last one is not news. Only
        // what a reader would see differently counts as a change.
        return known.Advert.players != advert.players ||
               known.Advert.maxPlayers != advert.maxPlayers ||
               known.Advert.name != advert.name;
    }

    private bool ForgetTheSilent()
    {
        float deadline = Time.unscaledTime - ForgetAfterSeconds;

        expired.Clear();

        foreach (KeyValuePair<string, Entry> pair in byAddress)
        {
            if (pair.Value.SeenAt < deadline)
                expired.Add(pair.Key);
        }

        for (int i = 0; i < expired.Count; i++)
        {
            byAddress.Remove(expired[i]);

            for (int j = ordered.Count - 1; j >= 0; j--)
            {
                if (ordered[j].Address == expired[i])
                    ordered.RemoveAt(j);
            }
        }

        return expired.Count > 0;
    }
}
