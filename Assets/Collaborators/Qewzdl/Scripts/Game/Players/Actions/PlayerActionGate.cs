using System;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerActionGate : MonoBehaviour, IPlayerActionGate
{
    private object activeOwner;

    // The carry that stepped aside so its holder could get into a cupboard.
    //
    // One slot was right for three actions that genuinely cannot happen at
    // once, and then wrong for the one pair that can. Picking something up is
    // an action and it ends; carrying it afterwards is a state, and the gate
    // is also the server's record of who holds what - TryPickUpServer opens it
    // and the drop RPC closes it - so a carry cannot simply be let go of at
    // the moment it is confirmed.
    //
    // A player holding a torch who climbs into a wardrobe is not doing two
    // things at once in any sense a player would recognise. So the carry is
    // put down here for as long as the hiding lasts and picked back up when it
    // ends, and everything that asks the gate what is going on gets the one
    // answer that is true: hiding.
    private PlayerActionKind suspendedAction;
    private object suspendedOwner;

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

        if (!IsBusy)
        {
            return true;
        }

        if (ActiveAction == action && ReferenceEquals(activeOwner, owner))
        {
            return true;
        }

        return CanSuspend(action);
    }

    public bool TryBegin(PlayerActionKind action, object owner)
    {
        if (!CanBegin(action, owner))
        {
            return false;
        }

        Take(action, owner);
        return true;
    }

    public void Confirm(PlayerActionKind action, object owner)
    {
        Validate(action, owner);

        // A confirmation comes from server-authoritative replicated state. It
        // deliberately supersedes a client-side pending prediction whose RPC
        // lost the race on the server.
        Take(action, owner);
    }

    public bool End(PlayerActionKind action, object owner)
    {
        Validate(action, owner);

        if (ActiveAction == action &&
            ReferenceEquals(activeOwner, owner))
        {
            Restore();
            return true;
        }

        // The item left while its holder was hidden - dropped, despawned, or
        // taken off them. It is not the action in progress, but it is still
        // this gate's to forget, and forgetting it is what stops a carry that
        // no longer exists from coming back when they climb out.
        if (suspendedAction == action &&
            ReferenceEquals(suspendedOwner, owner))
        {
            ClearSuspended();
            return true;
        }

        return false;
    }

    private void OnDisable()
    {
        Clear();
    }

    // Only hiding, and only over a carry. Drag is left out on purpose: a
    // player hauling something cannot reach a hiding place to begin with -
    // interaction refuses to focus anything while a drag is on - so making
    // room for it here would be answering a question nobody asks.
    private bool CanSuspend(PlayerActionKind action)
    {
        return action == PlayerActionKind.Hiding &&
               ActiveAction == PlayerActionKind.Pickup;
    }

    private void Take(PlayerActionKind action, object owner)
    {
        if (CanSuspend(action))
        {
            suspendedAction = ActiveAction;
            suspendedOwner = activeOwner;
        }

        ActiveAction = action;
        activeOwner = owner;
    }

    private void Restore()
    {
        ActiveAction = suspendedAction;
        activeOwner = suspendedOwner;
        ClearSuspended();
    }

    private void ClearSuspended()
    {
        suspendedAction = PlayerActionKind.None;
        suspendedOwner = null;
    }

    private void Clear()
    {
        ActiveAction = PlayerActionKind.None;
        activeOwner = null;
        ClearSuspended();
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
