using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class DoorOccupiedVolume : MonoBehaviour
{
    [SerializeField] private DoorInteractableObject linkedDoor;
    [SerializeField] private Collider occupiedVolume;
    [SerializeField] private LayerMask actorLayerMask = ~0;
    [SerializeField] private bool includePlayers = true;
    [SerializeField] private bool includeEnemies = true;
    [SerializeField] private bool includeOtherNetworkObjects;

    private readonly List<Collider> registeredActorColliders = new();

    private void Awake()
    {
        CacheComponents();

        if (linkedDoor == null)
        {
            Debug.LogError(
                $"{nameof(DoorOccupiedVolume)} requires a linked {nameof(DoorInteractableObject)}.",
                this
            );
        }

        if (occupiedVolume == null)
        {
            Debug.LogError(
                $"{nameof(DoorOccupiedVolume)} requires an occupied-volume collider.",
                this
            );
            return;
        }

        if (!occupiedVolume.isTrigger)
        {
            Debug.LogWarning(
                $"{nameof(DoorOccupiedVolume)} collider should be configured as trigger.",
                this
            );
        }
    }

    private void OnDisable()
    {
        if (linkedDoor == null)
        {
            registeredActorColliders.Clear();
            return;
        }

        for (int i = registeredActorColliders.Count - 1; i >= 0; i--)
        {
            linkedDoor.UnregisterOccupyingActor(registeredActorColliders[i]);
        }

        registeredActorColliders.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        RegisterActor(other);
    }

    private void OnTriggerStay(Collider other)
    {
        RegisterActor(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!CanEvaluateServer() ||
            linkedDoor == null ||
            !TryResolveActorCollider(other, out Collider actorCollider))
        {
            return;
        }

        if (!registeredActorColliders.Remove(actorCollider))
        {
            return;
        }

        linkedDoor.UnregisterOccupyingActor(actorCollider);
    }

    private void RegisterActor(Collider other)
    {
        if (!CanEvaluateServer() ||
            linkedDoor == null ||
            !TryResolveActorCollider(other, out Collider actorCollider))
        {
            return;
        }

        if (registeredActorColliders.Contains(actorCollider))
        {
            return;
        }

        registeredActorColliders.Add(actorCollider);
        linkedDoor.RegisterOccupyingActor(actorCollider);
    }

    private bool TryResolveActorCollider(Collider source, out Collider actorCollider)
    {
        actorCollider = null;

        if (source == null || source == occupiedVolume)
        {
            return false;
        }

        if (!IsLayerInMask(source.gameObject.layer, actorLayerMask))
        {
            return false;
        }

        if (includePlayers &&
            (source.GetComponentInParent<PlayerNetwork>() != null ||
             source.GetComponentInParent<PlayerController>() != null))
        {
            actorCollider = source;
            return true;
        }

        if (includeEnemies && source.GetComponentInParent<NetworkEnemyController>() != null)
        {
            actorCollider = source;
            return true;
        }

        if (includeOtherNetworkObjects &&
            source.GetComponentInParent<NetworkObject>() != null)
        {
            actorCollider = source;
            return true;
        }

        return false;
    }

    private void CacheComponents()
    {
        if (occupiedVolume == null)
        {
            occupiedVolume = GetComponent<Collider>();
        }

        if (linkedDoor == null)
        {
            linkedDoor = GetComponentInParent<DoorInteractableObject>();
        }
    }

    private static bool CanEvaluateServer()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager == null ||
               !networkManager.IsListening ||
               networkManager.IsServer;
    }

    private static bool IsLayerInMask(int layer, LayerMask layerMask)
    {
        return (layerMask.value & (1 << layer)) != 0;
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();

        if (occupiedVolume != null)
        {
            occupiedVolume.isTrigger = true;
        }
    }

    private void OnValidate()
    {
        CacheComponents();
    }
#endif
}
