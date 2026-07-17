using System;

internal sealed class SessionServiceReadinessMonitor : IDisposable
{
    private readonly ISessionServiceRegistry registry;
    private readonly Func<GameState> stateProvider;
    private readonly Action<string> readinessLost;
    private readonly GameStateMachine stateMachine;
    private bool failureRaised;
    private bool disposed;

    internal SessionServiceReadinessMonitor(
        ISessionServiceRegistry serviceRegistry,
        Func<GameState> currentStateProvider,
        Action<string> readinessLostHandler)
    {
        registry = serviceRegistry ??
                   throw new ArgumentNullException(nameof(serviceRegistry));
        stateProvider = currentStateProvider ??
                        throw new ArgumentNullException(nameof(currentStateProvider));
        readinessLost = readinessLostHandler ??
                        throw new ArgumentNullException(nameof(readinessLostHandler));

        if (registry.IsDisposed)
            throw new ObjectDisposedException(nameof(serviceRegistry));

        registry.ServicesChanged += HandleServicesChanged;
    }

    internal SessionServiceReadinessMonitor(
        ISessionServiceRegistry serviceRegistry,
        GameStateMachine gameStateMachine,
        Action<string> readinessLostHandler)
        : this(
            serviceRegistry,
            CreateStateProvider(gameStateMachine),
            readinessLostHandler)
    {
        stateMachine = gameStateMachine;
        stateMachine.StateChanged += HandleGameStateChanged;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        registry.ServicesChanged -= HandleServicesChanged;

        if (stateMachine != null)
            stateMachine.StateChanged -= HandleGameStateChanged;
    }

    private void HandleServicesChanged()
    {
        ValidateNow();
    }

    private void HandleGameStateChanged(GameState previousState, GameState newState)
    {
        ValidateNow();
    }

    internal bool ValidateNow()
    {
        if (disposed || failureRaised)
            return false;

        GameState currentState = stateProvider.Invoke();

        // Connecting and LoadingGame are composition windows. Dynamic NGO
        // services may not have spawned on a joining client yet; the explicit
        // client/server readiness gates validate them before committing Lobby
        // or InGame. The monitor owns health only after that commit.
        if (currentState != GameState.Lobby &&
            currentState != GameState.InGame)
        {
            return true;
        }

        if (SessionServiceReadinessPolicy.Validate(
                currentState,
                registry,
                out string error) &&
            ValidateActiveServerPhase(currentState, out error))
        {
            return true;
        }

        failureRaised = true;
        readinessLost.Invoke(error);
        return false;
    }

    private bool ValidateActiveServerPhase(GameState currentState, out string error)
    {
        ProjectSceneKind expectedScene = currentState switch
        {
            GameState.Lobby => ProjectSceneKind.Lobby,
            GameState.InGame => ProjectSceneKind.Game,
            _ => ProjectSceneKind.Unknown
        };

        if (expectedScene == ProjectSceneKind.Unknown)
        {
            error = string.Empty;
            return true;
        }

        return SessionServiceReadinessPolicy.ValidateServerPhase(
            expectedScene,
            registry,
            out error);
    }

    private static Func<GameState> CreateStateProvider(
        GameStateMachine gameStateMachine)
    {
        if (gameStateMachine == null)
            throw new ArgumentNullException(nameof(gameStateMachine));

        return () => gameStateMachine.CurrentState;
    }
}
