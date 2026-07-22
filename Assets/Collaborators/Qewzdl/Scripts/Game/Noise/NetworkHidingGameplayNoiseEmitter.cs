using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(HidingPlaceInteractable))]
public sealed class NetworkHidingGameplayNoiseEmitter : NetworkBehaviour
{
    [SerializeField] private HidingPlaceInteractable hidingPlace;

    private IGameplayNoiseService noiseService;
    private bool missingNoiseServiceLogged;

    public override void OnNetworkSpawn()
    {
        ResolveReferences();
        Subscribe();
    }

    public override void OnNetworkDespawn()
    {
        Unsubscribe();
        noiseService = null;
        missingNoiseServiceLogged = false;
    }

    public override void OnDestroy()
    {
        Unsubscribe();
        base.OnDestroy();
    }

    private void HandleServerNoiseRequested(
        HidingNoiseCue cue,
        PlayerHidingController playerHiding
    )
    {
        if (!IsServer ||
            !IsSpawned ||
            hidingPlace == null ||
            playerHiding == null ||
            !TryResolveNoiseService())
        {
            return;
        }

        HidingPlaceData settings = hidingPlace.Configuration;

        if (settings == null)
        {
            return;
        }

        float radius = cue == HidingNoiseCue.Enter
            ? settings.EnterNoiseRadius
            : settings.ExitNoiseRadius;
        float loudness = cue == HidingNoiseCue.Enter
            ? settings.EnterNoiseLoudness
            : settings.ExitNoiseLoudness;

        if (radius <= 0f || loudness <= 0f)
        {
            return;
        }

        noiseService.TryRaiseNoiseServer(
            hidingPlace.EnemyInvestigationPosition,
            radius,
            loudness,
            GameplayNoiseSourceType.Player,
            playerHiding.NetworkObjectId,
            playerHiding.OwnerClientId,
            hidingPlace
        );
    }

    private bool TryResolveNoiseService()
    {
        if (noiseService != null && noiseService.IsInitialized)
        {
            missingNoiseServiceLogged = false;
            return true;
        }

        if (NetworkObjectServiceContext.TryResolveSessionService(
                NetworkManager,
                out noiseService) &&
            noiseService != null &&
            noiseService.IsInitialized)
        {
            missingNoiseServiceLogged = false;
            return true;
        }

        if (!missingNoiseServiceLogged)
        {
            missingNoiseServiceLogged = true;
            Debug.LogError(
                $"{nameof(NetworkHidingGameplayNoiseEmitter)} requires " +
                $"an initialized {nameof(IGameplayNoiseService)} from " +
                "its NetworkObject context.",
                this
            );
        }

        return false;
    }

    private void Subscribe()
    {
        if (hidingPlace == null)
        {
            return;
        }

        hidingPlace.ServerNoiseRequested -=
            HandleServerNoiseRequested;
        hidingPlace.ServerNoiseRequested +=
            HandleServerNoiseRequested;
    }

    private void Unsubscribe()
    {
        if (hidingPlace != null)
        {
            hidingPlace.ServerNoiseRequested -=
                HandleServerNoiseRequested;
        }
    }

    private void ResolveReferences()
    {
        if (hidingPlace == null)
        {
            hidingPlace = GetComponent<HidingPlaceInteractable>();
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        ResolveReferences();
    }

    private void OnValidate()
    {
        ResolveReferences();
    }
#endif
}
