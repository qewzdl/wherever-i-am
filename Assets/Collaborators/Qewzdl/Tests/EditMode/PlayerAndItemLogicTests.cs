using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

internal sealed class PlayerComponentInitializationProbe :
    PlayerComponent,
    IPlayerSignalListener
{
    internal int InitializeCount { get; private set; }
    internal int CleanupCount { get; private set; }
    internal bool ReceivedMultiplayerFlag { get; private set; }
    internal bool ReceivedOwnerFlag { get; private set; }
    internal PlayerSignals ReceivedSignals { get; private set; }
    internal PlayerStates ReceivedStates { get; private set; }

    protected override void OnPostInit(
        PlayerOrchestrator orchestrator,
        bool isMultiplayer,
        bool isOwner)
    {
        InitializeCount++;
        ReceivedMultiplayerFlag = isMultiplayer;
        ReceivedOwnerFlag = isOwner;
        ReceivedSignals = signals;
        ReceivedStates = states;
    }

    public void Cleanup()
    {
        CleanupCount++;
    }
}

internal sealed class PlayerNetworkInitializationProbe : PlayerNetworkComponent
{
    internal int InitializeCount { get; private set; }
    internal PlayerSignals ReceivedSignals { get; private set; }

    protected override void OnPostInit(PlayerOrchestrator orchestrator)
    {
        InitializeCount++;
        ReceivedSignals = signals;
    }
}

[Category("Baseline")]
public sealed class PlayerAndItemLogicTests
{
    private const string ProductionPlayerPrefabPath =
        "Assets/Collaborators/6aTowKa/Prefabs/Player.prefab";

    [Test]
    public void ProductionPlayerPrefab_ContainsHidingRuntimeAndScopeBinding()
    {
        GameObject playerPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductionPlayerPrefabPath
            );

        Assert.That(playerPrefab, Is.Not.Null);

        PlayerHidingController hidingController =
            playerPrefab.GetComponent<PlayerHidingController>();
        PlayerInteraction interaction =
            playerPrefab.GetComponent<PlayerInteraction>();
        PlayerScopeLifetime scopeLifetime =
            playerPrefab.GetComponent<PlayerScopeLifetime>();

        Assert.That(hidingController, Is.Not.Null);
        Assert.That(interaction, Is.Not.Null);
        Assert.That(scopeLifetime, Is.Not.Null);

        SerializedObject interactionObject = new(interaction);
        SerializedObject scopeObject = new(scopeLifetime);

        Assert.That(
            interactionObject
                .FindProperty("playerHidingController")
                .objectReferenceValue,
            Is.SameAs(hidingController)
        );
        Assert.That(
            scopeObject
                .FindProperty("hidingStateService")
                .objectReferenceValue,
            Is.SameAs(hidingController)
        );
    }

    [Test]
    public void PlayerSignals_RegisterUniqueNamedSignalsAndDispatchListeners()
    {
        PlayerSignals signals = new();

        Assert.That(signals.SignalsList.Count, Is.EqualTo(9));
        Assert.That(
            signals.SignalsList.Cast<BasePlayerSignal>().Select(signal => signal.DebugName),
            Is.Unique);

        int triggerCount = 0;
        Vector2 lastMove = default;

        void HandleInteract()
        {
            triggerCount++;
        }

        void HandleMove(Vector2 movement)
        {
            lastMove = movement;
        }

        signals.Interact.Listen(HandleInteract);
        signals.MoveSignal.Listen(HandleMove);

        signals.Interact.Trigger();
        signals.MoveSignal.Trigger(new Vector2(2f, 3f));

        Assert.That(triggerCount, Is.EqualTo(1));
        Assert.That(lastMove, Is.EqualTo(new Vector2(2f, 3f)));
        Assert.That(signals.Interact.GetListeners().Length, Is.EqualTo(1));

        signals.Interact.Unlisten(HandleInteract);
        signals.MoveSignal.Unlisten(HandleMove);

        Assert.That(signals.Interact.GetListeners(), Is.Null);
        Assert.That(signals.MoveSignal.GetListeners(), Is.Null);
    }

    [Test]
    public void PlayerOrchestrator_SetupInitializesOnlyEnabledComponents()
    {
        GameObject playerObject = new("Player orchestrator test");

        try
        {
            PlayerOrchestrator orchestrator =
                playerObject.AddComponent<PlayerOrchestrator>();
            PlayerComponentInitializationProbe enabledProbe =
                playerObject.AddComponent<PlayerComponentInitializationProbe>();
            PlayerComponentInitializationProbe disabledProbe =
                playerObject.AddComponent<PlayerComponentInitializationProbe>();
            PlayerNetworkInitializationProbe networkProbe =
                playerObject.AddComponent<PlayerNetworkInitializationProbe>();
            disabledProbe.enabled = false;

            orchestrator.Setup(isMultiplayer: true, isOwner: false);

            Assert.That(enabledProbe.InitializeCount, Is.EqualTo(1));
            Assert.That(enabledProbe.ReceivedMultiplayerFlag, Is.True);
            Assert.That(enabledProbe.ReceivedOwnerFlag, Is.False);
            Assert.That(enabledProbe.ReceivedSignals, Is.SameAs(orchestrator.Signals));
            Assert.That(enabledProbe.ReceivedStates, Is.SameAs(orchestrator.States));
            Assert.That(disabledProbe.InitializeCount, Is.Zero);
            Assert.That(networkProbe.InitializeCount, Is.EqualTo(1));
            Assert.That(networkProbe.ReceivedSignals, Is.SameAs(orchestrator.Signals));
        }
        finally
        {
            Object.DestroyImmediate(playerObject);
        }
    }

    [Test]
    public void PlayerStates_KeepInteractionStatesIndependent()
    {
        PlayerStates states = new();

        states.IsDragging = true;
        Assert.That(states.IsDragging, Is.True);
        Assert.That(states.IsCarrying, Is.False);

        states.IsCarrying = true;
        states.IsDragging = false;

        Assert.That(states.IsDragging, Is.False);
        Assert.That(states.IsCarrying, Is.True);
        Assert.That(states.IsHiding, Is.False);

        states.IsHiding = true;
        states.IsCarrying = false;

        Assert.That(states.IsHiding, Is.True);
        Assert.That(states.IsDragging, Is.False);
        Assert.That(states.IsCarrying, Is.False);
    }

    [Test]
    public void ViewmodelEntry_RoundTripsTransformPoseAndScale()
    {
        GameObject sourceObject = new("Viewmodel source");
        GameObject targetObject = new("Viewmodel target");

        try
        {
            sourceObject.transform.localPosition = new Vector3(1f, 2f, 3f);
            sourceObject.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
            sourceObject.transform.localScale = new Vector3(2f, 3f, 4f);

            ViewmodelItemEntry entry = new();
            entry.SetFrom(sourceObject.transform);
            entry.ApplyTo(targetObject.transform);

            Assert.That(
                targetObject.transform.localPosition,
                Is.EqualTo(sourceObject.transform.localPosition));
            Assert.That(
                Quaternion.Angle(
                    targetObject.transform.localRotation,
                    sourceObject.transform.localRotation),
                Is.LessThan(0.001f));
            Assert.That(
                targetObject.transform.localScale,
                Is.EqualTo(sourceObject.transform.localScale));
        }
        finally
        {
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(targetObject);
        }
    }
}
