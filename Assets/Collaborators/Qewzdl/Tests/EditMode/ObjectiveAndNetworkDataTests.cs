using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[Category("Gameplay")]
public sealed class ObjectiveAndNetworkDataTests
{
    [Test]
    public void ObjectiveDefinition_ValidatesIdentityProgressAndCompletion()
    {
        ObjectiveDefinition objective = CreateObjective(
            "escape",
            2.5f,
            ObjectiveCompletionPolicy.CompletesGame,
            GameResultType.Victory);

        try
        {
            TestReflection.SetField(objective, "title", "");

            Assert.That(objective.IsValid(out string error), Is.True, error);
            Assert.That(objective.DisplayName, Is.EqualTo("escape"));
            Assert.That(objective.RequiredProgress, Is.EqualTo(2.5f));
            Assert.That(objective.TargetValue, Is.EqualTo(3));
            Assert.That(objective.CompletesGame, Is.True);
            Assert.That(objective.ResultType, Is.EqualTo(GameResultType.Victory));
        }
        finally
        {
            Object.DestroyImmediate(objective);
        }
    }

    [Test]
    public void ObjectiveDefinition_RejectsEveryInvalidShape()
    {
        ObjectiveDefinition objective = CreateObjective(
            "",
            1f,
            ObjectiveCompletionPolicy.CompletesGame,
            GameResultType.Victory);

        try
        {
            Assert.That(objective.IsValid(out string error), Is.False);
            StringAssert.Contains("empty objective id", error);

            TestReflection.SetField(objective, "objectiveId", new string('я', 31));
            Assert.That(objective.IsValid(out error), Is.False);
            StringAssert.Contains("too long", error);

            TestReflection.SetField(objective, "objectiveId", "escape");
            TestReflection.SetField(objective, "requiredProgress", 0f);
            Assert.That(objective.IsValid(out error), Is.False);
            StringAssert.Contains("greater than zero", error);

            TestReflection.SetField(objective, "requiredProgress", 1f);
            TestReflection.SetField(objective, "completionResult", GameResultType.None);
            Assert.That(objective.IsValid(out error), Is.False);
            StringAssert.Contains("invalid completion result", error);
        }
        finally
        {
            Object.DestroyImmediate(objective);
        }
    }

    [Test]
    public void ObjectiveOnlyDefinition_NeverProducesMatchResult()
    {
        ObjectiveDefinition objective = CreateObjective(
            "switch",
            1f,
            ObjectiveCompletionPolicy.ObjectiveOnly,
            GameResultType.Victory);

        try
        {
            Assert.That(objective.IsValid(out string error), Is.True, error);
            Assert.That(objective.CompletesGame, Is.False);
            Assert.That(objective.ResultType, Is.EqualTo(GameResultType.None));
            Assert.That(
                MatchOutcomeFactory.FromObjective(objective, 4).HasResult,
                Is.False);
        }
        finally
        {
            Object.DestroyImmediate(objective);
        }
    }

    [Test]
    public void ObjectiveSequence_RejectsMissingNullAndDuplicateDefinitions()
    {
        ObjectiveSequenceDefinition sequence =
            ScriptableObject.CreateInstance<ObjectiveSequenceDefinition>();
        ObjectiveDefinition first = CreateObjective(
            "door",
            1f,
            ObjectiveCompletionPolicy.ObjectiveOnly,
            GameResultType.None);
        ObjectiveDefinition duplicate = CreateObjective(
            "door",
            1f,
            ObjectiveCompletionPolicy.CompletesGame,
            GameResultType.Victory);

        try
        {
            TestReflection.SetField(sequence, "objectives", null);
            Assert.That(sequence.IsValid(out string error), Is.False);
            StringAssert.Contains("has no objectives", error);

            TestReflection.SetField(
                sequence,
                "objectives",
                new ObjectiveDefinition[] { first, null });
            Assert.That(sequence.IsValid(out error), Is.False);
            StringAssert.Contains("null objective", error);

            TestReflection.SetField(
                sequence,
                "objectives",
                new[] { first, duplicate });
            Assert.That(sequence.IsValid(out error), Is.False);
            StringAssert.Contains("duplicate objective id", error);
        }
        finally
        {
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(duplicate);
        }
    }

    [Test]
    public void ObjectiveSequence_ProvidesStableOrderedLookup()
    {
        ObjectiveSequenceDefinition sequence =
            ScriptableObject.CreateInstance<ObjectiveSequenceDefinition>();
        ObjectiveDefinition first = CreateObjective(
            "switch",
            2f,
            ObjectiveCompletionPolicy.ObjectiveOnly,
            GameResultType.None);
        ObjectiveDefinition final = CreateObjective(
            "escape",
            1f,
            ObjectiveCompletionPolicy.CompletesGame,
            GameResultType.Victory);

        try
        {
            TestReflection.SetField(sequence, "objectives", new[] { first, final });

            Assert.That(sequence.IsValid(out string error), Is.True, error);
            Assert.That(sequence.Count, Is.EqualTo(2));
            Assert.That(sequence.GetObjective(0), Is.SameAs(first));
            Assert.That(sequence.GetObjective(1), Is.SameAs(final));
            Assert.That(sequence.GetObjective(-1), Is.Null);
            Assert.That(sequence.GetObjective(2), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(final);
        }
    }

    [Test]
    public void ReplicatedGameplayData_RoundTripsWithoutLosingFields()
    {
        LobbyPlayerData lobbyPlayer = new(9, "Alice", true);
        LobbyPlayerData lobbyPlayerCopy = RoundTrip(lobbyPlayer);
        Assert.That(lobbyPlayerCopy, Is.EqualTo(lobbyPlayer));

        ChatMessageData chat = new(
            71,
            9,
            "Alice",
            "Ready",
            ChatChannel.Game,
            42.5);
        ChatMessageData chatCopy = RoundTrip(chat);
        Assert.That(chatCopy, Is.EqualTo(chat));

        GameResultData result = GameResultData.Create(
            GameResultType.Victory,
            MatchResultSource.Objective,
            "escape",
            "Door opened",
            9);
        GameResultData resultCopy = RoundTrip(result);
        Assert.That(resultCopy, Is.EqualTo(result));

        ObjectiveNetworkState objective = new()
        {
            ObjectiveId = "escape",
            SequenceIndex = 2,
            State = ObjectiveRuntimeState.Active,
            Progress01 = 0.75f
        };
        ObjectiveNetworkState objectiveCopy = RoundTrip(objective);
        Assert.That(objectiveCopy, Is.EqualTo(objective));
    }

    [Test]
    public void EnemyAttackFrames_RoundTripAndConvertBackToDomainEvents()
    {
        EnemyAttackPhaseEvent phaseEvent = new(
            EnemyAttackPhase.AttackCommit,
            EnemyTargetIdentity.None,
            new Vector3(1f, 2f, 3f),
            new Vector3(4f, 5f, 6f),
            EnemyAttackResultType.LineOfHitBlocked);

        EnemyAttackPresentationFrame presentation =
            EnemyAttackPresentationFrame.FromPhaseEvent(phaseEvent, 7);
        EnemyAttackPresentationFrame presentationCopy = RoundTrip(presentation);
        EnemyAttackPhaseEvent eventCopy = presentationCopy.ToPhaseEvent();

        Assert.That(presentationCopy, Is.EqualTo(presentation));
        Assert.That(presentationCopy.HasValue, Is.True);
        Assert.That(eventCopy.Phase, Is.EqualTo(phaseEvent.Phase));
        Assert.That(eventCopy.AttackerPosition, Is.EqualTo(phaseEvent.AttackerPosition));
        Assert.That(eventCopy.TargetPosition, Is.EqualTo(phaseEvent.TargetPosition));
        Assert.That(eventCopy.Reason, Is.EqualTo(phaseEvent.Reason));

        EnemyAttackResult result = EnemyAttackResult.Create(
            EnemyAttackResultType.Hit,
            EnemyTargetIdentity.None,
            Vector3.left,
            Vector3.right);
        EnemyAttackResolutionFrame resolution =
            EnemyAttackResolutionFrame.FromResult(result, 8);
        EnemyAttackResolutionFrame resolutionCopy = RoundTrip(resolution);

        Assert.That(resolutionCopy, Is.EqualTo(resolution));
        Assert.That(resolutionCopy.ToResult().Type, Is.EqualTo(result.Type));
        Assert.That(resolutionCopy.ToResult().TargetPosition, Is.EqualTo(Vector3.right));
    }

    [Test]
    public void ObjectiveAndResultNoneStates_AreUnambiguouslyEmpty()
    {
        Assert.That(ObjectiveNetworkState.None.HasObjective, Is.False);
        Assert.That(ObjectiveNetworkState.None.SequenceIndex, Is.EqualTo(-1));
        Assert.That(ObjectiveNetworkState.None.State, Is.EqualTo(ObjectiveRuntimeState.None));

        Assert.That(GameResultData.None.HasResult, Is.False);
        Assert.That(GameResultData.None.Source, Is.EqualTo(MatchResultSource.None));
        Assert.That(GameResultData.None.SourceId.IsEmpty, Is.True);
    }

    private static ObjectiveDefinition CreateObjective(
        string id,
        float requiredProgress,
        ObjectiveCompletionPolicy policy,
        GameResultType result)
    {
        ObjectiveDefinition objective =
            ScriptableObject.CreateInstance<ObjectiveDefinition>();
        TestReflection.SetField(objective, "objectiveId", id);
        TestReflection.SetField(objective, "requiredProgress", requiredProgress);
        TestReflection.SetField(objective, "completionPolicy", policy);
        TestReflection.SetField(objective, "completionResult", result);
        TestReflection.SetField(objective, "completionReason", "Completed");
        TestReflection.SetField(objective, "requiresSceneBinding", false);
        return objective;
    }

    private static T RoundTrip<T>(T value)
        where T : struct, INetworkSerializable
    {
        using FastBufferWriter writer = new(1024, Allocator.Temp);
        writer.WriteNetworkSerializable(value);

        using FastBufferReader reader = new(writer, Allocator.Temp);
        T copy = default;
        reader.ReadNetworkSerializableInPlace(ref copy);
        return copy;
    }
}
