using NUnit.Framework;
using UnityEngine;

// One slot for three actions, and the one pair that has to share it.
//
// Carrying something is not an action a player would say they were doing
// instead of hiding, but the gate is also the server's record of who holds
// what, so the carry cannot just be dropped when it is confirmed. It waits
// instead. Everything here is about that wait: that it happens, that it ends,
// and that it does not bring back an item that has gone.
public sealed class PlayerActionGateTests
{
    private GameObject host;
    private PlayerActionGate gate;
    private object item;
    private object hiding;

    [SetUp]
    public void SetUp()
    {
        host = new GameObject("Player Action Gate Test");
        gate = host.AddComponent<PlayerActionGate>();
        item = new object();
        hiding = new object();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(host);
    }

    [Test]
    public void ACarriedItemDoesNotKeepAPlayerOutOfAHidingPlace()
    {
        Assert.That(gate.TryBegin(PlayerActionKind.Pickup, item), Is.True);

        Assert.That(gate.CanBegin(PlayerActionKind.Hiding, hiding), Is.True);
        Assert.That(gate.TryBegin(PlayerActionKind.Hiding, hiding), Is.True);
        Assert.That(gate.ActiveAction, Is.EqualTo(PlayerActionKind.Hiding));
    }

    [Test]
    public void TheCarryComesBackWhenTheHidingEnds()
    {
        gate.TryBegin(PlayerActionKind.Pickup, item);
        gate.TryBegin(PlayerActionKind.Hiding, hiding);

        Assert.That(gate.End(PlayerActionKind.Hiding, hiding), Is.True);
        Assert.That(gate.ActiveAction, Is.EqualTo(PlayerActionKind.Pickup));
        Assert.That(gate.End(PlayerActionKind.Pickup, item), Is.True);
        Assert.That(gate.IsBusy, Is.False);
    }

    // The replicated path takes the slot the same way the requested one does,
    // because a client whose RPC lost the race still has to end up hidden.
    [Test]
    public void AConfirmedHidingStandsTheCarryDownToo()
    {
        gate.TryBegin(PlayerActionKind.Pickup, item);
        gate.Confirm(PlayerActionKind.Hiding, hiding);

        Assert.That(gate.ActiveAction, Is.EqualTo(PlayerActionKind.Hiding));

        gate.End(PlayerActionKind.Hiding, hiding);
        Assert.That(gate.ActiveAction, Is.EqualTo(PlayerActionKind.Pickup));
    }

    // Dropped, despawned, or taken off them. Whatever happened, an item that
    // left while its holder was hidden must not be waiting for them outside.
    [Test]
    public void AnItemThatLeavesWhileHiddenDoesNotComeBack()
    {
        gate.TryBegin(PlayerActionKind.Pickup, item);
        gate.TryBegin(PlayerActionKind.Hiding, hiding);

        Assert.That(gate.End(PlayerActionKind.Pickup, item), Is.True);
        Assert.That(gate.ActiveAction, Is.EqualTo(PlayerActionKind.Hiding));

        gate.End(PlayerActionKind.Hiding, hiding);
        Assert.That(gate.IsBusy, Is.False);
    }

    // Only that one pair shares the slot. A second item, a drag, or somebody
    // else's hiding are all still refused.
    [Test]
    public void NothingElseGetsToShareTheSlot()
    {
        gate.TryBegin(PlayerActionKind.Pickup, item);

        Assert.That(gate.CanBegin(PlayerActionKind.Drag, new object()), Is.False);
        Assert.That(gate.CanBegin(PlayerActionKind.Pickup, new object()), Is.False);

        gate.TryBegin(PlayerActionKind.Hiding, hiding);

        Assert.That(gate.CanBegin(PlayerActionKind.Pickup, new object()), Is.False);
        Assert.That(gate.CanBegin(PlayerActionKind.Drag, new object()), Is.False);
        Assert.That(gate.CanBegin(PlayerActionKind.Hiding, new object()), Is.False);
    }

    // A drag cannot step aside: interaction refuses to focus anything while
    // one is on, so a hiding place is out of reach to begin with.
    [Test]
    public void ADragStillBlocksHiding()
    {
        gate.TryBegin(PlayerActionKind.Drag, item);

        Assert.That(gate.CanBegin(PlayerActionKind.Hiding, hiding), Is.False);
        Assert.That(gate.TryBegin(PlayerActionKind.Hiding, hiding), Is.False);
        Assert.That(gate.ActiveAction, Is.EqualTo(PlayerActionKind.Drag));
    }

    // What both halves of "no dropping in a cupboard" read. While the carry is
    // standing aside, the gate does not report it as the thing being done -
    // and that is the difference between an item leaving because its holder
    // asked and one leaving because it was taken off them.
    [Test]
    public void AWaitingCarryIsNotReportedAsTheActionInProgress()
    {
        gate.TryBegin(PlayerActionKind.Pickup, item);
        Assert.That(gate.IsActive(PlayerActionKind.Pickup), Is.True);

        gate.TryBegin(PlayerActionKind.Hiding, hiding);

        Assert.That(gate.IsActive(PlayerActionKind.Hiding), Is.True);
        Assert.That(gate.IsActive(PlayerActionKind.Pickup), Is.False);

        gate.End(PlayerActionKind.Hiding, hiding);
        Assert.That(gate.IsActive(PlayerActionKind.Pickup), Is.True);
    }

    // Somebody else's token never releases a carry that is waiting.
    [Test]
    public void OnlyTheHoldersOwnTokenForgetsAWaitingCarry()
    {
        gate.TryBegin(PlayerActionKind.Pickup, item);
        gate.TryBegin(PlayerActionKind.Hiding, hiding);

        Assert.That(gate.End(PlayerActionKind.Pickup, new object()), Is.False);

        gate.End(PlayerActionKind.Hiding, hiding);
        Assert.That(gate.ActiveAction, Is.EqualTo(PlayerActionKind.Pickup));
    }
}
