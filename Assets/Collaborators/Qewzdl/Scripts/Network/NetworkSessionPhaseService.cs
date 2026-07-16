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

    private IDisposable serviceRegistration;

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

        return NetworkObjectServiceContext.TryRegisterRequiredSessionServices(
            this,
            registration => registration.Register<ISessionPhaseService>(this),
            out serviceRegistration);
    }

    private void UnregisterSessionService()
    {
        IDisposable registration = serviceRegistration;
        serviceRegistration = null;
        registration?.Dispose();
    }
}
