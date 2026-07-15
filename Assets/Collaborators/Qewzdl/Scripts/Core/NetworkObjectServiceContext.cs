using Unity.Netcode;
using UnityEngine;

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

    public static bool TryGetSpawnedComponent<TComponent>(
        NetworkManager networkManager,
        out TComponent component)
        where TComponent : Component
    {
        component = null;

        if (networkManager == null || networkManager.SpawnManager == null)
            return false;

        foreach (NetworkObject networkObject in networkManager.SpawnManager.SpawnedObjectsList)
        {
            if (networkObject != null && networkObject.TryGetComponent(out component))
                return true;
        }

        component = null;
        return false;
    }
}
