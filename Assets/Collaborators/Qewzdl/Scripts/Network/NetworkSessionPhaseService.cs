using System;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class NetworkSessionPhaseService : NetworkBehaviour,
    ISessionPhaseService,
    ISessionServiceReadiness
{
    private readonly NetworkVariable<ProjectSceneKind> serverScenePhase =
        new(
            ProjectSceneKind.Unknown,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private SessionServiceRegistration serviceRegistration;

    ProjectSceneKind ISessionPhaseService.ServerScenePhase => serverScenePhase.Value;

    bool ISessionServiceReadiness.IsSessionServiceReady =>
        IsSpawned && isActiveAndEnabled;

    public override void OnNetworkSpawn()
    {
        DontDestroyOnLoad(gameObject);

        if (RegisterSessionService())
            return;

        enabled = false;
    }

    public override void OnNetworkDespawn()
    {
        UnregisterSessionService();
    }

    public override void OnDestroy()
    {
        UnregisterSessionService();
        base.OnDestroy();
    }

    bool ISessionPhaseService.TrySetServerScenePhase(ProjectSceneKind sceneKind)
    {
        if (!IsSpawned || !IsServer ||
            (sceneKind != ProjectSceneKind.Lobby &&
             sceneKind != ProjectSceneKind.Game))
        {
            return false;
        }

        serverScenePhase.Value = sceneKind;
        return true;
    }

    private bool RegisterSessionService()
    {
        if (serviceRegistration != null)
            return true;

        if (NetworkManager == null)
        {
            Debug.LogError(
                $"{nameof(NetworkSessionPhaseService)} has no owning {nameof(NetworkManager)}.",
                this);
            return false;
        }

        NetworkSessionOrchestrator orchestrator =
            NetworkManager.GetComponent<NetworkSessionOrchestrator>();

        if (orchestrator == null)
        {
            Debug.LogError(
                $"{nameof(NetworkSessionPhaseService)} requires " +
                $"{nameof(NetworkSessionOrchestrator)} on the {nameof(NetworkManager)} object.",
                this);
            return false;
        }

        if (orchestrator.TryRegisterSessionServices(
                registrar => registrar.Register<ISessionPhaseService>(this),
                out serviceRegistration,
                out Exception failure))
        {
            return true;
        }

        Debug.LogError(
            $"{nameof(NetworkSessionPhaseService)} failed to register the authoritative " +
            "Session phase contract.",
            this);

        if (failure != null)
            Debug.LogException(failure, this);

        _ = orchestrator.ReportSessionReadinessFailureAsync(
            nameof(NetworkSessionPhaseService),
            failure?.Message ?? "Failed to register ISessionPhaseService.");
        return false;
    }

    private void UnregisterSessionService()
    {
        SessionServiceRegistration registration = serviceRegistration;
        serviceRegistration = null;
        registration?.Dispose();
    }
}
