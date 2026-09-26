// What kind of session this is, decided once when it starts.
//
// A singleplayer game is still a host - every system in the game is networked,
// and a second, offline way of running them would be a second game to keep
// working. What makes it singleplayer is that nobody else can get in: it
// listens on this machine only, is never announced on the network, and admits
// one player. Everything that exists only because there could be others - the
// roster, readiness, the door, spectating somebody else - reads this and steps
// aside.
public enum SessionMode
{
    Multiplayer,
    Singleplayer
}

// What kind of session is running, as a service of that session: registered
// when its scope opens and gone when it closes, so there is no "previous
// session's mode" for anybody to read by mistake.
//
// It answers questions about the session - who can reach it, what the lobby
// offers. Questions about the match - is anybody else here to watch, was I the
// only one caught - are asked of the match's players instead: a multiplayer
// lobby can be started by one person, and it should play exactly like a
// singleplayer game does.
public interface INetworkSessionInfo
{
    SessionMode Mode { get; }
}

internal sealed class NetworkSessionInfo : INetworkSessionInfo
{
    public NetworkSessionInfo(SessionMode mode)
    {
        Mode = mode;
    }

    public SessionMode Mode { get; }
}
