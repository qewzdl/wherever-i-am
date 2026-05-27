using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkSessionStateMachine : MonoBehaviour
{
    public NetworkSessionState CurrentState { get; private set; } = NetworkSessionState.Offline;

    public event Action<NetworkSessionState, NetworkSessionState> StateChanged;

    public bool CanStartConnection =>
        CurrentState == NetworkSessionState.Offline ||
        CurrentState == NetworkSessionState.Failed;

    public bool CanStartGame => CurrentState == NetworkSessionState.Lobby;

    public bool IsActiveSession =>
        CurrentState == NetworkSessionState.StartingHost ||
        CurrentState == NetworkSessionState.StartingClient ||
        CurrentState == NetworkSessionState.Lobby ||
        CurrentState == NetworkSessionState.LoadingGame ||
        CurrentState == NetworkSessionState.InGame;

    public bool TryChangeState(NetworkSessionState newState, string reason = "")
    {
        if (CurrentState == newState)
            return true;

        if (!CanTransition(CurrentState, newState))
        {
            Debug.LogError(
                $"{nameof(NetworkSessionStateMachine)} rejected transition {CurrentState} -> {newState}. Reason: {reason}",
                this);

            return false;
        }

        NetworkSessionState previousState = CurrentState;
        CurrentState = newState;

        StateChanged?.Invoke(previousState, newState);

        if (string.IsNullOrWhiteSpace(reason))
            Debug.Log($"Network session state changed: {previousState} -> {newState}", this);
        else
            Debug.Log($"Network session state changed: {previousState} -> {newState}. Reason: {reason}", this);

        return true;
    }

    public bool CanTransition(NetworkSessionState from, NetworkSessionState to)
    {
        if (from == to)
            return true;

        switch (from)
        {
            case NetworkSessionState.Offline:
                return to == NetworkSessionState.StartingHost ||
                       to == NetworkSessionState.StartingClient;

            case NetworkSessionState.StartingHost:
                return to == NetworkSessionState.Lobby ||
                       to == NetworkSessionState.Disconnecting ||
                       to == NetworkSessionState.Failed;

            case NetworkSessionState.StartingClient:
                return to == NetworkSessionState.Lobby ||
                       to == NetworkSessionState.Disconnecting ||
                       to == NetworkSessionState.Failed;

            case NetworkSessionState.Lobby:
                return to == NetworkSessionState.LoadingGame ||
                       to == NetworkSessionState.Disconnecting ||
                       to == NetworkSessionState.Failed;

            case NetworkSessionState.LoadingGame:
                return to == NetworkSessionState.InGame ||
                       to == NetworkSessionState.Disconnecting ||
                       to == NetworkSessionState.Failed;

            case NetworkSessionState.InGame:
                return to == NetworkSessionState.Disconnecting ||
                       to == NetworkSessionState.Failed;

            case NetworkSessionState.Disconnecting:
                return to == NetworkSessionState.Offline ||
                       to == NetworkSessionState.Failed;

            case NetworkSessionState.Failed:
                return to == NetworkSessionState.Offline ||
                       to == NetworkSessionState.StartingHost ||
                       to == NetworkSessionState.StartingClient;
        }

        return false;
    }
}