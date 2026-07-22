using System;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerActionGate : MonoBehaviour, IPlayerActionGate
{
    private object activeOwner;

    public bool IsBusy => ActiveAction != PlayerActionKind.None;
    public PlayerActionKind ActiveAction { get; private set; }

    public bool IsActive(PlayerActionKind action)
    {
        return action != PlayerActionKind.None &&
               ActiveAction == action;
    }

    public bool CanBegin(PlayerActionKind action, object owner)
    {
        Validate(action, owner);

        return !IsBusy ||
               (ActiveAction == action &&
                ReferenceEquals(activeOwner, owner));
    }

    public bool TryBegin(PlayerActionKind action, object owner)
    {
        if (!CanBegin(action, owner))
        {
            return false;
        }

        ActiveAction = action;
        activeOwner = owner;
        return true;
    }

    public void Confirm(PlayerActionKind action, object owner)
    {
        Validate(action, owner);

        // A confirmation comes from server-authoritative replicated state. It
        // deliberately supersedes a client-side pending prediction whose RPC
        // lost the race on the server.
        ActiveAction = action;
        activeOwner = owner;
    }

    public bool End(PlayerActionKind action, object owner)
    {
        Validate(action, owner);

        if (ActiveAction != action ||
            !ReferenceEquals(activeOwner, owner))
        {
            return false;
        }

        Clear();
        return true;
    }

    private void OnDisable()
    {
        Clear();
    }

    private void Clear()
    {
        ActiveAction = PlayerActionKind.None;
        activeOwner = null;
    }

    private static void Validate(
        PlayerActionKind action,
        object owner
    )
    {
        if (action == PlayerActionKind.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(action),
                action,
                "None cannot be acquired as a player action."
            );
        }

        if (ReferenceEquals(owner, null))
        {
            throw new ArgumentNullException(nameof(owner));
        }
    }
}

internal static class PlayerActionGateContext
{
    internal static bool TryGet(
        NetworkManager networkManager,
        ulong clientId,
        out IPlayerActionGate actionGate
    )
    {
        actionGate = null;

        if (networkManager == null ||
            !networkManager.IsServer ||
            !networkManager.ConnectedClients.TryGetValue(
                clientId,
                out NetworkClient client) ||
            client.PlayerObject == null)
        {
            return false;
        }

        PlayerActionGate gate =
            client.PlayerObject.GetComponent<PlayerActionGate>();

        if (gate == null || !gate.isActiveAndEnabled)
        {
            return false;
        }

        actionGate = gate;
        return true;
    }

    internal static bool TryBegin(
        NetworkManager networkManager,
        ulong clientId,
        PlayerActionKind action,
        object owner,
        out IPlayerActionGate actionGate
    )
    {
        return TryGet(networkManager, clientId, out actionGate) &&
               actionGate.TryBegin(action, owner);
    }

    internal static bool TryEnd(
        NetworkManager networkManager,
        ulong clientId,
        PlayerActionKind action,
        object owner
    )
    {
        return TryGet(networkManager, clientId, out IPlayerActionGate gate) &&
               gate.End(action, owner);
    }
}
