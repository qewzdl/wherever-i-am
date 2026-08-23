using System;

// Read-only view of the global network session lifecycle. UI may observe it,
// but only the session flow and shutdown coordinator may move it.
public interface INetworkSessionReadService
{
    NetworkSessionState CurrentState { get; }

    event Action<NetworkSessionState, NetworkSessionState> StateChanged;
}
