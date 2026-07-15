using System;

internal sealed class SessionServiceReadinessMonitor : IDisposable
{
    private readonly ISessionServiceRegistry registry;
    private readonly Func<GameState> stateProvider;
    private readonly Action<string> readinessLost;
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

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        registry.ServicesChanged -= HandleServicesChanged;
    }

    private void HandleServicesChanged()
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
}
