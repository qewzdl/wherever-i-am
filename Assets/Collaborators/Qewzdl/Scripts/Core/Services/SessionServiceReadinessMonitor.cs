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
        ValidateCurrentState();
    }

    private void HandleGameStateChanged(GameState previousState, GameState newState)
    {
        ValidateCurrentState();
    }

    private void ValidateCurrentState()
    {
        if (disposed || failureRaised ||
            SessionServiceReadinessPolicy.Validate(
                stateProvider.Invoke(),
                registry,
                out string error))
        {
            return;
        }

        failureRaised = true;
        readinessLost.Invoke(error);
    }

    private static Func<GameState> CreateStateProvider(
        GameStateMachine gameStateMachine)
    {
        if (gameStateMachine == null)
            throw new ArgumentNullException(nameof(gameStateMachine));

        return () => gameStateMachine.CurrentState;
    }
}
