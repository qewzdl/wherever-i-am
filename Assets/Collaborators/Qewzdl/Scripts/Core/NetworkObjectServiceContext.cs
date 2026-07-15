using Unity.Netcode;

public static class NetworkObjectServiceContext
{
    public static bool TryResolveSessionService<TService>(
        NetworkManager networkManager,
        out TService service)
        where TService : class
    {
        service = null;

        if (networkManager == null)
            return false;

        NetworkSessionOrchestrator orchestrator =
            networkManager.GetComponent<NetworkSessionOrchestrator>();

        IServiceResolver services = orchestrator != null
            ? orchestrator.SessionServices
            : null;

        return services != null && services.TryResolve(out service);
    }
}
