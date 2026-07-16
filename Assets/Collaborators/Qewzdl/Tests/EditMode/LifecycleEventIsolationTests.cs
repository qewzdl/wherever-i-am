using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class LifecycleEventIsolationTests
{
    [Test]
    public async Task ShutdownRecovery_RetriesTimeoutInImmediateMode()
    {
        List<NetworkShutdownMode> modes = new();

        await NetworkShutdownRecoveryPolicy.ExecuteAsync(
            mode =>
            {
                modes.Add(mode);
                return modes.Count == 1
                    ? Task.FromException(new TimeoutException("Injected timeout."))
                    : Task.CompletedTask;
            },
            NetworkShutdownMode.Graceful,
            1);

        Assert.That(
            modes,
            Is.EqualTo(new[]
            {
                NetworkShutdownMode.Graceful,
                NetworkShutdownMode.Immediate
            }));
    }

    [Test]
    public void ShutdownRecovery_RemainsFailClosedAfterRetryExhaustion()
    {
        int attemptCount = 0;

        TimeoutException failure = Assert.ThrowsAsync<TimeoutException>(async () =>
            await NetworkShutdownRecoveryPolicy.ExecuteAsync(
                _ =>
                {
                    attemptCount++;
                    return Task.FromException(
                        new TimeoutException("Injected timeout."));
                },
                NetworkShutdownMode.Graceful,
                2));

        Assert.That(attemptCount, Is.EqualTo(3));
        Assert.That(failure.Message, Does.Contain("fail-closed"));
    }

    [Test]
    public void StateCommits_AreNotInterruptedByThrowingSubscribers()
    {
        GameObject root = new("Lifecycle event isolation test");
        bool previousIgnoreState = LogAssert.ignoreFailingMessages;
        LogAssert.ignoreFailingMessages = true;

        try
        {
            GameStateMachine gameState = root.AddComponent<GameStateMachine>();
            NetworkSessionStateMachine sessionState =
                root.AddComponent<NetworkSessionStateMachine>();
            int gameSubscriberCount = 0;
            int sessionSubscriberCount = 0;

            gameState.StateChanged += (_, _) =>
                throw new InvalidOperationException("Injected game subscriber failure.");
            gameState.StateChanged += (_, _) => gameSubscriberCount++;
            sessionState.StateChanged += (_, _) =>
                throw new InvalidOperationException("Injected session subscriber failure.");
            sessionState.StateChanged += (_, _) => sessionSubscriberCount++;

            Assert.DoesNotThrow(() => gameState.ChangeState(GameState.MainMenu));
            Assert.DoesNotThrow(() => sessionState.TryChangeState(
                NetworkSessionState.StartingClient,
                "Isolation test."));

            Assert.That(gameState.CurrentState, Is.EqualTo(GameState.MainMenu));
            Assert.That(
                sessionState.CurrentState,
                Is.EqualTo(NetworkSessionState.StartingClient));
            Assert.That(gameSubscriberCount, Is.EqualTo(1));
            Assert.That(sessionSubscriberCount, Is.EqualTo(1));
        }
        finally
        {
            LogAssert.ignoreFailingMessages = previousIgnoreState;
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void SceneOperationCancellation_ClearsClientReadinessTracking()
    {
        GameObject root = new("Client readiness cancellation test");
        root.SetActive(false);

        try
        {
            AppRuntime runtime = root.AddComponent<AppRuntime>();
            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo kindField = typeof(AppRuntime).GetField(
                "pendingClientReadinessKind",
                flags);
            FieldInfo handleField = typeof(AppRuntime).GetField(
                "pendingClientReadinessSceneHandle",
                flags);

            Assert.That(kindField, Is.Not.Null);
            Assert.That(handleField, Is.Not.Null);
            kindField.SetValue(runtime, ProjectSceneKind.Game);
            handleField.SetValue(runtime, 1234);

            ((IProjectSceneLoadCompletionGate)runtime).CancelPending(
                ProjectOperationCancelReason.SessionShutdown);

            Assert.That(
                kindField.GetValue(runtime),
                Is.EqualTo(ProjectSceneKind.Unknown));
            Assert.That(handleField.GetValue(runtime), Is.EqualTo(0));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
