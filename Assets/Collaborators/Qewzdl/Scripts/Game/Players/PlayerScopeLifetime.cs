using System;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class PlayerScopeLifetime : NetworkBehaviour, IPlayerNetworkService
{
    [Header("Replicated Services")]
    [SerializeField] private PlayerNetwork replicatedStateService;
    [SerializeField] private PlayerEnemyAttackReceiver enemyAttackReceiver;

    [Header("Local-only Services")]
    [SerializeField] private PlayerInputHandler inputService;
    [SerializeField] private CameraLook cameraService;
    [SerializeField] private PlayerUI presentationService;

    private IDisposable scopeRegistration;

    private void Awake()
    {
        ResolveReferences();
    }

    public override void OnNetworkSpawn()
    {
        bool createLocalScope = IsLocalPlayer;

        if (!ValidateReferences(createLocalScope))
            return;

        Action<NetworkObjectServiceContext.RegistrationContext> registerLocalServices =
            createLocalScope
            ? registration =>
            {
                registration.Register<ILocalPlayerInputService>(inputService);
                registration.Register<ILocalPlayerCameraService>(cameraService);
                registration.Register<ILocalPlayerPresentationService>(presentationService);
            }
            : null;

        if (NetworkObjectServiceContext.TryOpenRequiredPlayerScope(
                this,
                registration =>
                {
                    registration.Register<IPlayerNetworkService>(this);
                    registration.Register<IReplicatedPlayerStateService>(replicatedStateService);
                    registration.Register<IEnemyAttackReceiver>(enemyAttackReceiver);
                },
                registerLocalServices,
                out scopeRegistration))
        {
            return;
        }
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
        IDisposable registration = scopeRegistration;
        scopeRegistration = null;
        registration?.Dispose();
    }

    private bool ValidateReferences(bool requireLocalServices)
    {
        ResolveReferences();
        bool valid = true;
        valid &= ValidateReference(replicatedStateService, nameof(replicatedStateService));
        valid &= ValidateReference(enemyAttackReceiver, nameof(enemyAttackReceiver));

        if (requireLocalServices)
        {
            valid &= ValidateReference(inputService, nameof(inputService));
            valid &= ValidateReference(cameraService, nameof(cameraService));
            valid &= ValidateReference(presentationService, nameof(presentationService));
        }

        return valid;
    }

    private void ResolveReferences()
    {
        if (replicatedStateService == null)
            replicatedStateService = GetComponent<PlayerNetwork>();

        if (enemyAttackReceiver == null)
            enemyAttackReceiver = GetComponent<PlayerEnemyAttackReceiver>();

        if (inputService == null)
            inputService = GetComponent<PlayerInputHandler>();

        if (cameraService == null)
            cameraService = GetComponentInChildren<CameraLook>(true);

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
