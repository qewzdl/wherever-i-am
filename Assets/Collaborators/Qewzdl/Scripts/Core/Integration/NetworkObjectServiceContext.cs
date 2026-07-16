using System;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

public static class NetworkObjectServiceContext
{
    /// <summary>
    /// A short-lived registration batch passed only while a NetworkObject scope
    /// transaction is active. Do not cache this context.
    /// </summary>
    public sealed class RegistrationContext
    {
        private IServiceRegistrar registrar;

        internal RegistrationContext(IServiceRegistrar serviceRegistrar)
        {
            registrar = serviceRegistrar ??
                        throw new ArgumentNullException(nameof(serviceRegistrar));
        }

        /// <summary>
        /// Registers a Unity-owned service by its interface contract.
        /// </summary>
        public void Register<TContract>(TContract service)
            where TContract : class
        {
            IServiceRegistrar activeRegistrar = registrar;

            if (activeRegistrar == null)
            {
                throw new InvalidOperationException(
                    "The NetworkObject service registration batch is no longer active.");
            }

            activeRegistrar.Register<TContract>(service);
        }

        internal void Close()
        {
            registrar = null;
        }
    }

    public static bool TryResolveSessionService<TService>(
        NetworkBehaviour owner,
        out TService service)
        where TService : class
    {
        service = null;

        return owner != null &&
               TryResolveSessionService(owner.NetworkManager, out service);
    }

    public static bool TryResolveSessionService<TService>(
        NetworkManager networkManager,
        out TService service)
        where TService : class
    {
        service = null;

        return TryGetSessionServices(networkManager, out IServiceResolver services) &&
               services.TryResolve(out service);
    }

    /// <summary>
    /// Atomically registers one or more Session contracts for a spawned
    /// NetworkBehaviour. Dispose the returned handle in OnNetworkDespawn.
    /// </summary>
    public static bool TryRegisterSessionServices(
        NetworkBehaviour owner,
        Action<RegistrationContext> registerServices,
        out IDisposable registration,
        out Exception failure)
    {
        registration = null;

        if (registerServices == null)
        {
            failure = new ArgumentNullException(nameof(registerServices));
            return false;
        }

        if (!TryGetSpawnedOwnerOrchestrator(
                owner,
                out NetworkSessionOrchestrator orchestrator,
                out failure))
        {
            return false;
        }

        bool registered = orchestrator.TryRegisterSessionServices(
            registrar => RunRegistrationBatch(registrar, registerServices),
            out SessionServiceRegistration serviceRegistration,
            out failure);

        registration = serviceRegistration;
        return registered;
    }

    /// <summary>
    /// Registers a required dynamic Session service batch. A failed batch is
    /// reported to the coordinated readiness/shutdown pipeline automatically.
    /// </summary>
    public static bool TryRegisterRequiredSessionServices(
        NetworkBehaviour owner,
        Action<RegistrationContext> registerServices,
        out IDisposable registration)
    {
        if (TryRegisterSessionServices(
                owner,
                registerServices,
                out registration,
                out Exception failure))
        {
            return true;
        }

        ReportRequiredRegistrationFailure(
            owner,
            failure,
            "register required Session services",
            "The required Session service registration batch failed.");
        return false;
    }

    /// <summary>
    /// Reports loss of a required dynamic Session service to the coordinated
    /// shutdown owner.
    /// </summary>
    public static Task ReportSessionReadinessFailureAsync(
        NetworkBehaviour owner,
        string details)
    {
        if (owner == null || owner.NetworkManager == null)
            return Task.CompletedTask;

        NetworkSessionOrchestrator orchestrator =
            owner.NetworkManager.GetComponent<NetworkSessionOrchestrator>();

        return orchestrator != null
            ? orchestrator.ReportSessionReadinessFailureAsync(
                owner.GetType().Name,
                details)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Atomically creates replicated and, for the owner client, local Player
    /// scopes for a spawned player NetworkBehaviour. Dispose the returned
    /// handle in OnNetworkDespawn.
    /// </summary>
    public static bool TryOpenPlayerScope(
        NetworkBehaviour owner,
        Action<RegistrationContext> registerReplicatedServices,
        Action<RegistrationContext> registerLocalServices,
        out IDisposable registration,
        out Exception failure)
    {
        registration = null;

        if (registerReplicatedServices == null)
        {
            failure = new ArgumentNullException(nameof(registerReplicatedServices));
            return false;
        }

        if (!TryGetSpawnedOwnerOrchestrator(
                owner,
                out NetworkSessionOrchestrator orchestrator,
                out failure))
        {
            return false;
        }

        if (owner.IsLocalPlayer && registerLocalServices == null)
        {
            failure = new ArgumentNullException(nameof(registerLocalServices));
            return false;
        }

        Action<IServiceRegistrar> localRegistration = owner.IsLocalPlayer
            ? registrar => RunRegistrationBatch(registrar, registerLocalServices)
            : null;

        bool opened = orchestrator.TryOpenPlayerScope(
            owner.NetworkObjectId,
            owner.OwnerClientId,
            owner.IsLocalPlayer,
            registrar => RunRegistrationBatch(registrar, registerReplicatedServices),
            localRegistration,
            out PlayerScopeRegistration scopeRegistration,
            out failure);

        registration = scopeRegistration;
        return opened;
    }

    /// <summary>
    /// Opens a required Player scope. A failed scope transaction is reported
    /// to the coordinated readiness/shutdown pipeline automatically.
    /// </summary>
    public static bool TryOpenRequiredPlayerScope(
        NetworkBehaviour owner,
        Action<RegistrationContext> registerReplicatedServices,
        Action<RegistrationContext> registerLocalServices,
        out IDisposable registration)
    {
        if (TryOpenPlayerScope(
                owner,
                registerReplicatedServices,
                registerLocalServices,
                out registration,
                out Exception failure))
        {
            return true;
        }

        ReportRequiredRegistrationFailure(
            owner,
            failure,
            "open its required Player scope",
            "The required Player scope registration failed.");
        return false;
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

    private static bool TryGetSpawnedOwnerOrchestrator(
        NetworkBehaviour owner,
        out NetworkSessionOrchestrator orchestrator,
        out Exception failure)
    {
        orchestrator = null;
        failure = null;

        if (owner == null)
        {
            failure = new ArgumentNullException(nameof(owner));
            return false;
        }

        if (!owner.IsSpawned)
        {
            failure = new InvalidOperationException(
                $"{owner.GetType().Name} can register scoped services only after " +
                $"{nameof(NetworkBehaviour.OnNetworkSpawn)}.");

            return false;
        }

        NetworkManager networkManager = owner.NetworkManager;

        if (networkManager == null)
        {
            failure = new InvalidOperationException(
                $"{owner.GetType().Name} has no owning {nameof(NetworkManager)}.");

            return false;
        }

        orchestrator = networkManager.GetComponent<NetworkSessionOrchestrator>();

        if (orchestrator != null)
            return true;

        failure = new InvalidOperationException(
            $"{owner.GetType().Name} requires {nameof(NetworkSessionOrchestrator)} " +
            $"on the owning {nameof(NetworkManager)} object.");

        return false;
    }

    private static void RunRegistrationBatch(
        IServiceRegistrar registrar,
        Action<RegistrationContext> registerServices)
    {
        if (registerServices == null)
        {
            throw new ArgumentNullException(nameof(registerServices));
        }

        RegistrationContext context = new(registrar);

        try
        {
            registerServices.Invoke(context);
        }
        finally
        {
            context.Close();
        }
    }

    private static void ReportRequiredRegistrationFailure(
        NetworkBehaviour owner,
        Exception failure,
        string operation,
        string fallbackDetails)
    {
        string ownerName = owner != null
            ? owner.GetType().Name
            : "Unknown NetworkBehaviour";
        string details = failure?.Message ?? fallbackDetails;

        Debug.LogError(
            $"{ownerName} failed to {operation}: {details}",
            owner);

        if (failure != null)
            Debug.LogException(failure, owner);

        _ = ReportSessionReadinessFailureAsync(owner, details);
    }
}
