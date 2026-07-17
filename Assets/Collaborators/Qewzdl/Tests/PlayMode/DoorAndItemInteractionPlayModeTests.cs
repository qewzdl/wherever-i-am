using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

internal sealed class TestActivatorItem : PassiveItem
{
    internal int ActivationCount { get; private set; }

    public override void Activate()
    {
        ActivationCount++;
    }
}

internal sealed class TestUsableItem : UsableItem
{
    internal int UseCount { get; private set; }

    public override void Use()
    {
        UseCount++;
    }
}

internal sealed class TestItemRequiredInteractable : ItemRequiredInteractable
{
    internal int SuccessCount { get; private set; }
    internal int FailureCount { get; private set; }

    protected override void InteractWith(IActivator activator)
    {
        SuccessCount++;
        activator.Activate();
    }

    protected override void UnsuccessfulInteract()
    {
        FailureCount++;
    }
}

[Category("Gameplay")]
public sealed class DoorAndItemInteractionPlayModeTests
{
    private readonly List<Object> cleanup = new();

    [TearDown]
    public void TearDown()
    {
        DraggableObject.ActiveDraggedObjects.Clear();

        for (int i = cleanup.Count - 1; i >= 0; i--)
        {
            if (cleanup[i] != null)
                Object.DestroyImmediate(cleanup[i]);
        }

        cleanup.Clear();
    }

    [Test]
    public void Door_OpenCloseAndLineOfSight_AreDeterministicOffline()
    {
        DoorInteractableObject door = CreateDoor(out BoxCollider collider);

        Assert.That(door.CurrentState, Is.EqualTo(DoorState.Closed));
        Assert.That(door.BlocksLineOfSight, Is.True);
        Assert.That(DoorInteractableObject.LineOfSightBlockers, Does.Contain(door));
        Assert.That(
            door.TryBlockLineOfSight(new Ray(new Vector3(0f, 0f, -2f), Vector3.forward), 5f),
            Is.True);

        Assert.That(door.TryOpen(), Is.True);
        Assert.That(door.CurrentState, Is.EqualTo(DoorState.Open));
        Assert.That(door.BlocksLineOfSight, Is.False);
        Assert.That(door.TryClose(), Is.True);
        Assert.That(door.CurrentState, Is.EqualTo(DoorState.Closed));

        collider.isTrigger = true;
        Assert.That(
            door.TryBlockLineOfSight(new Ray(new Vector3(0f, 0f, -2f), Vector3.forward), 5f),
            Is.False);
    }

    [Test]
    public void Door_OccupancyBlocksCloseUntilActorLeaves()
    {
        DoorInteractableObject door = CreateDoor(out _);
        BoxCollider actor = Track(new GameObject("Door occupant")).AddComponent<BoxCollider>();

        Assert.That(door.TryOpen(), Is.True);
        door.RegisterOccupyingActor(actor);

        Assert.That(door.CurrentState, Is.EqualTo(DoorState.Blocked));
        Assert.That(door.CanClose, Is.False);
        Assert.That(door.TryClose(), Is.False);

        door.UnregisterOccupyingActor(actor);

        Assert.That(door.CurrentState, Is.EqualTo(DoorState.Open));
        Assert.That(door.CanClose, Is.True);
        Assert.That(door.TryClose(), Is.True);
    }

    [Test]
    public void Door_EnemyReservationOwnsTheTransitionAndCanRollback()
    {
        DoorInteractableObject door = CreateDoor(out _);

        Assert.That(door.TryBeginEnemyOpen(), Is.True);
        Assert.That(door.CurrentState, Is.EqualTo(DoorState.Opening));
        Assert.That(door.IsReservedByEnemy, Is.True);
        Assert.That(door.TryOpen(), Is.False);

        Assert.That(door.TryCancelEnemyOpen(), Is.True);
        Assert.That(door.CurrentState, Is.EqualTo(DoorState.Closed));
        Assert.That(door.IsReservedByEnemy, Is.False);

        Assert.That(door.TryBeginEnemyOpen(), Is.True);
        Assert.That(door.TryCompleteEnemyOpen(0f), Is.True);
        Assert.That(door.CurrentState, Is.EqualTo(DoorState.ForcedOpen));
        Assert.That(door.TryClose(), Is.False);
    }

    [UnityTest]
    public IEnumerator Door_TimedTransitionCompletesOnUpdate()
    {
        DoorInteractableObject door = CreateDoor(out _);
        PlayModeTestReflection.SetField(door, "transitionDuration", 0.02f);

        Assert.That(door.TryOpen(), Is.True);
        Assert.That(door.CurrentState, Is.EqualTo(DoorState.Opening));

        yield return new WaitForSeconds(0.04f);

        Assert.That(door.CurrentState, Is.EqualTo(DoorState.Open));
    }

    [Test]
    public void RequiredItem_OnlyAcceptsMatchingActivator()
    {
        TestActivatorItem correct = CreatePickup<TestActivatorItem>(17);
        TestActivatorItem wrong = CreatePickup<TestActivatorItem>(18);
        TestUsableItem nonActivator = CreatePickup<TestUsableItem>(17);
        TestItemRequiredInteractable target =
            Track(new GameObject("Required item target"))
                .AddComponent<TestItemRequiredInteractable>();
        PlayModeTestReflection.SetField(target, "requiredItemID", 17);

        target.OnInteract(new InteractionContext { CurrentItem = correct });
        target.OnInteract(new InteractionContext { CurrentItem = wrong });
        LogAssert.Expect(
            LogType.Warning,
            "Current item does not implement IActivator interface.");
        target.OnInteract(new InteractionContext { CurrentItem = nonActivator });
        target.OnInteract(new InteractionContext { CurrentItem = null });

        Assert.That(target.SuccessCount, Is.EqualTo(1));
        Assert.That(target.FailureCount, Is.EqualTo(3));
        Assert.That(correct.ActivationCount, Is.EqualTo(1));
        Assert.That(wrong.ActivationCount, Is.Zero);
        Assert.That(correct.GetItemID(), Is.EqualTo(17));
    }

    [Test]
    public void PlayerInteraction_TracksCarryAndDragStatesWithoutStaleFlags()
    {
        GameObject player = Track(new GameObject("Interaction state player"));
        PlayerInteraction interaction = player.AddComponent<PlayerInteraction>();
        PlayerOrchestrator orchestrator = player.AddComponent<PlayerOrchestrator>();
        TestActivatorItem item = CreatePickup<TestActivatorItem>(1);
        TestActivatorItem draggable = CreatePickup<TestActivatorItem>(2);

        orchestrator.Setup(isMultiplayer: false, isOwner: true);
        interaction.SetCurrentItem(item);
        Assert.That(orchestrator.States.IsCarrying, Is.True);

        interaction.ConfirmDragging(draggable);
        Assert.That(orchestrator.States.IsDragging, Is.True);

        interaction.SetCurrentItem(null);
        interaction.Undrag();
        Assert.That(orchestrator.States.IsCarrying, Is.False);
        Assert.That(orchestrator.States.IsDragging, Is.False);
    }

    private DoorInteractableObject CreateDoor(out BoxCollider collider)
    {
        GameObject gameObject = Track(new GameObject("Test door"));
        collider = gameObject.AddComponent<BoxCollider>();
        collider.size = new Vector3(1f, 2f, 0.2f);
        return gameObject.AddComponent<DoorInteractableObject>();
    }

    private T CreatePickup<T>(int itemId)
        where T : PickupItem
    {
        PickupItemData data = Track(ScriptableObject.CreateInstance<PickupItemData>());
        data.ItemID = itemId;
        data.Mass = 2f;
        data.ThrowVelocitySamples = 2f;

        GameObject gameObject = Track(new GameObject(typeof(T).Name));
        gameObject.SetActive(false);
        gameObject.AddComponent<NetworkObject>();
        gameObject.AddComponent<Rigidbody>();
        gameObject.AddComponent<BoxCollider>();
        T item = gameObject.AddComponent<T>();
        PlayModeTestReflection.SetField(item, "data", data);
        gameObject.SetActive(true);
        return item;
    }

    private T Track<T>(T value)
        where T : Object
    {
        cleanup.Add(value);
        return value;
    }
}
