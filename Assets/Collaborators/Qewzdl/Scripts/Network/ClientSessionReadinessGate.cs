internal static class ClientSessionReadinessGate
{
    internal static bool Begin(
        NetworkSessionStateMachine stateMachine,
        ProjectSceneKind sceneKind,
        out string error)
    {
        if (stateMachine == null)
        {
            error = $"{nameof(NetworkSessionStateMachine)} is not configured.";
            return false;
        }

        NetworkSessionState currentState = stateMachine.CurrentState;

        switch (sceneKind)
        {
            case ProjectSceneKind.Lobby:
                // InGame is a finished match coming back to the lobby. The
                // state stays until Commit, because until the lobby's services
                // are there the client has not arrived anywhere.
                if (currentState == NetworkSessionState.StartingClient)
                {
                    bool changed = stateMachine.TryChangeState(
                        NetworkSessionState.LoadingLobby,
                        "Server synchronized the Lobby scene to this client.");
                    error = changed
                        ? string.Empty
                        : "Failed to enter client LoadingLobby state.";
                    return changed;
                }

                if (currentState == NetworkSessionState.LoadingLobby ||
                    currentState == NetworkSessionState.Lobby ||
                    currentState == NetworkSessionState.InGame)
                {
                    error = string.Empty;
                    return true;
                }

                break;

            case ProjectSceneKind.Game:
                if (currentState == NetworkSessionState.LoadingGame)
                {
                    error = string.Empty;
                    return true;
                }

                if (currentState == NetworkSessionState.StartingClient ||
                    currentState == NetworkSessionState.LoadingLobby ||
                    currentState == NetworkSessionState.Lobby)
                {
                    bool changed = stateMachine.TryChangeState(
                        NetworkSessionState.LoadingGame,
                        "Server synchronized the Game scene to this client.");
                    error = changed
                        ? string.Empty
                        : "Failed to enter client LoadingGame state.";
                    return changed;
                }

                break;
        }

        error =
            $"Client cannot begin readiness for scene '{sceneKind}' from " +
            $"Session state '{currentState}'.";
        return false;
    }

    internal static bool Commit(
        NetworkSessionStateMachine stateMachine,
        ProjectSceneKind sceneKind,
        IServiceResolver services,
        out string error)
    {
        if (stateMachine == null)
        {
            error = $"{nameof(NetworkSessionStateMachine)} is not configured.";
            return false;
        }

        if (!SessionServiceReadinessPolicy.Validate(
                sceneKind,
                services,
                out error) ||
            !SessionServiceReadinessPolicy.ValidateServerPhase(
                sceneKind,
                services,
                out error))
        {
            return false;
        }

        switch (sceneKind)
        {
            case ProjectSceneKind.Lobby:
                if (stateMachine.CurrentState == NetworkSessionState.Lobby)
                    return true;

                if ((stateMachine.CurrentState == NetworkSessionState.StartingClient ||
                     stateMachine.CurrentState == NetworkSessionState.LoadingLobby) &&
                    stateMachine.TryChangeState(
                        NetworkSessionState.Lobby,
                        "Client Lobby scene and dynamic services are ready."))
                {
                    return true;
                }

                if (stateMachine.CurrentState == NetworkSessionState.InGame &&
                    stateMachine.TryChangeState(
                        NetworkSessionState.Lobby,
                        "Client returned to the lobby after the match."))
                {
                    return true;
                }

                break;

            case ProjectSceneKind.Game:
                if (stateMachine.CurrentState == NetworkSessionState.InGame)
                    return true;

                if ((stateMachine.CurrentState == NetworkSessionState.StartingClient ||
                     stateMachine.CurrentState == NetworkSessionState.LoadingLobby) &&
                    !stateMachine.TryChangeState(
                        NetworkSessionState.LoadingGame,
                        "Late-joining client synchronized directly into Game."))
                {
                    break;
                }

                if (stateMachine.CurrentState == NetworkSessionState.LoadingGame &&
                    stateMachine.TryChangeState(
                        NetworkSessionState.InGame,
                        "Client Game scene and dynamic services are ready."))
                {
                    return true;
                }

                break;
        }

        error =
            $"Client cannot commit scene '{sceneKind}' from Session state " +
            $"'{stateMachine.CurrentState}'.";
        return false;
    }
}
