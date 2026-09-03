using System;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class PlayerScopeLifetime : NetworkBehaviour, IPlayerNetworkService
{
    [Header("Replicated Services")]
    [SerializeField] private PlayerNetwork replicatedStateService;
    [SerializeField] private PlayerActionGate actionGateService;
    [SerializeField] private PlayerHidingController hidingStateService;
    [SerializeField] private PlayerEnemyAttackReceiver enemyAttackReceiver;

    [Header("Local-only Services")]
    [SerializeField] private PlayerInputHandler inputService;
    [SerializeField] private CameraLook cameraService;
    [SerializeField] private PlayerController movementService;
    [SerializeField] private PlayerUI presentationService;

    private IDisposable scopeRegistration;

    private void Awake()
    {
        ResolveReferences();
    }

    public override void OnNetworkSpawn()
    {
        bool createLocalScope = IsLocalPlayer;
        ISettingsService settingsService = null;

        if (!ValidateReferences(createLocalScope))
            return;

        if (createLocalScope &&
            !NetworkObjectServiceContext.TryResolveSessionService(
                this,
                out settingsService))
        {
            Debug.LogError(
                $"{nameof(PlayerScopeLifetime)} could not resolve {nameof(ISettingsService)} for the local player.",
                this);
            _ = NetworkObjectServiceContext.ReportSessionReadinessFailureAsync(
                this,
                $"Local player is missing {nameof(ISettingsService)}.");
            return;
        }

        Action<NetworkObjectServiceContext.RegistrationContext> registerLocalServices =
            createLocalScope
            ? registration =>
            {
                registration.Register<IPlayerHidingCommandService>(
                    hidingStateService);
                registration.Register<ILocalPlayerInputService>(inputService);
                registration.Register<ILocalPlayerCameraService>(cameraService);
                registration.Register<ILocalPlayerPresentationService>(presentationService);
            }
            : null;

        if (!NetworkObjectServiceContext.TryOpenRequiredPlayerScope(
                this,
                registration =>
                {
                    registration.Register<IPlayerNetworkService>(this);
                    registration.Register<IPlayerActionGate>(
                        actionGateService);
                    registration.Register<IReplicatedPlayerStateService>(replicatedStateService);
                    registration.Register<IReplicatedPlayerHidingStateService>(hidingStateService);
                    registration.Register<IEnemyAttackReceiver>(enemyAttackReceiver);
                },
                registerLocalServices,
                out scopeRegistration))
        {
            return;
        }

        if (!createLocalScope)
            return;

        // The camera and the movement are the two things on a player that want
        // settings - one for how far it sees and how fast it turns, the other
        // for whether crouch is held or flipped - and this class already holds
        // both. Searching the object for whoever might implement an interface
        // was never anything but a way of not saying so.
        cameraService.Construct(settingsService);
        movementService.Construct(settingsService);
    }

    public override void OnNetworkDespawn()
    {
        CloseScope();
    }

    public override void OnDestroy()
    {
        CloseScope();
        base.OnDestroy();
    }

    private void CloseScope()
    {
        if (cameraService != null)
            cameraService.ReleaseSettingsService();

        if (movementService != null)
            movementService.ReleaseSettingsService();

        IDisposable registration = scopeRegistration;
        scopeRegistration = null;
        registration?.Dispose();
    }

    private bool ValidateReferences(bool requireLocalServices)
    {
        ResolveReferences();
        bool valid = true;
        valid &= ValidateReference(replicatedStateService, nameof(replicatedStateService));
        valid &= ValidateReference(actionGateService, nameof(actionGateService));
        valid &= ValidateReference(hidingStateService, nameof(hidingStateService));
        valid &= ValidateReference(enemyAttackReceiver, nameof(enemyAttackReceiver));

        if (requireLocalServices)
        {
            valid &= ValidateReference(inputService, nameof(inputService));
            valid &= ValidateReference(cameraService, nameof(cameraService));
            valid &= ValidateReference(movementService, nameof(movementService));
            valid &= ValidateReference(presentationService, nameof(presentationService));
        }

        return valid;
    }

    private void ResolveReferences()
    {
        if (replicatedStateService == null)
            replicatedStateService = GetComponent<PlayerNetwork>();

        if (actionGateService == null)
            actionGateService = GetComponent<PlayerActionGate>();

        if (enemyAttackReceiver == null)
            enemyAttackReceiver = GetComponent<PlayerEnemyAttackReceiver>();

        if (hidingStateService == null)
            hidingStateService = GetComponent<PlayerHidingController>();

        if (inputService == null)
            inputService = GetComponent<PlayerInputHandler>();

        if (cameraService == null)
            cameraService = GetComponentInChildren<CameraLook>(true);

        if (movementService == null)
            movementService = GetComponent<PlayerController>();

        if (presentationService == null)
            presentationService = GetComponent<PlayerUI>();
    }

    private bool ValidateReference(UnityEngine.Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError(
            $"{nameof(PlayerScopeLifetime)} is missing '{fieldName}'.",
            this);

        return false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveReferences();
    }
#endif
}
