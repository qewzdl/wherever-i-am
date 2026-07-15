using Unity.Netcode;

public static class NetworkObjectServiceContext
{
    public static bool TryResolveSessionService<TService>(
        NetworkManager networkManager,
        out TService service)
        where TService : class
    {
        service = null;

        return TryGetSessionServices(networkManager, out IServiceResolver services) &&
               services.TryResolve(out service);
    }

    internal static bool TryGetSessionServices(
        NetworkManager networkManager,
        out IServiceResolver services)
    {
        services = null;

        if (networkManager == null)
            return false;

        NetworkSessionOrchestrator orchestrator =
            networkManager.GetComponent<NetworkSessionOrchestrator>();

        services = orchestrator != null
            ? orchestrator.SessionServices
            : null;

        return services != null && !services.IsDisposed;
    }
}
