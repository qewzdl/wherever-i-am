using System;
using System.Collections.Generic;
using NUnit.Framework;

public sealed class ChildServiceContractPolicyTests
{
    private sealed class DynamicSessionService :
        IChatReadService,
        IChatCommandService,
        IMatchCompletionService
    {
        public event Action MessagesChanged
        {
            add { }
            remove { }
        }

        public event Action<ChatMessageData> MessageAdded
        {
            add { }
            remove { }
        }

        public event Action AvailabilityChanged
        {
            add { }
            remove { }
        }

        public bool CanSubmitMessages => true;
        public ChatChannel CurrentChannel => ChatChannel.Lobby;
        public int MessageCount => 0;
        public bool IsMatchRunning => true;

        public ChatMessageData GetMessage(int index)
        {
            return default;
        }

        public bool TryGetMessage(uint messageId, out ChatMessageData message)
        {
            message = default;
            return false;
        }

        public bool IsLocalClient(ulong clientId)
        {
            return false;
        }

        public void SubmitMessage(string text)
        {
        }

        public bool CompleteMatchServerOnly(GameResultData matchResult, string reason)
        {
            return true;
        }
    }

    private sealed class CrossScopeService : IChatCommandService, IPauseService
    {
        public bool IsPaused => false;

        public event Action<bool> PauseStateChanged
        {
            add { }
            remove { }
        }

        public void SubmitMessage(string text)
        {
        }

        public void Pause()
        {
        }

        public void Resume()
        {
        }

        public void TogglePause()
        {
        }
    }

    [Test]
    public void SessionPolicy_HasExactOwnershipAllowlist()
    {
        Type[] expectedContracts =
        {
            typeof(ISessionServiceRegistry),
            typeof(IPlayerScopeRegistry),
            typeof(IGameMapSessionService),
            typeof(IGameplayNoiseService),
            typeof(IChatReadService),
            typeof(IChatCommandService),
            typeof(IMatchCompletionService)
        };

        AssertExactPolicy(
            expectedContracts,
            SessionContractPolicy.AllowedContractCount,
            SessionContractPolicy.IsAllowed);
        Assert.That(SessionContractPolicy.IsAllowed(typeof(IPauseService)), Is.False);
        Assert.That(
            SessionContractPolicy.IsAllowed(typeof(IReplicatedPlayerStateService)),
            Is.False);
        Assert.That(
            SessionContractPolicy.IsAllowed(typeof(ILocalPlayerInputService)),
            Is.False);
    }

    [Test]
    public void ScenePolicies_AreExactPerSceneOwnership()
    {
        AssertExactPolicy(
            new[] { typeof(ILobbyReadService), typeof(ILobbyCommandService) },
            SceneContractPolicy.Lobby.AllowedContractCount,
            SceneContractPolicy.Lobby.IsAllowed);
        AssertExactPolicy(
            new[] { typeof(IPauseService) },
            SceneContractPolicy.Game.AllowedContractCount,
            SceneContractPolicy.Game.IsAllowed);
        AssertExactPolicy(
            Array.Empty<Type>(),
            SceneContractPolicy.MainMenu.AllowedContractCount,
            SceneContractPolicy.MainMenu.IsAllowed);
        AssertExactPolicy(
            Array.Empty<Type>(),
            SceneContractPolicy.Map.AllowedContractCount,
            SceneContractPolicy.Map.IsAllowed);

        Assert.That(SceneContractPolicy.Lobby.IsAllowed(typeof(IPauseService)), Is.False);
        Assert.That(SceneContractPolicy.Game.IsAllowed(typeof(ILobbyReadService)), Is.False);
        Assert.That(SceneContractPolicy.Game.IsAllowed(typeof(IChatReadService)), Is.False);
        Assert.That(SceneContractPolicy.Map.IsAllowed(typeof(IPauseService)), Is.False);
    }

    [Test]
    public void PlayerPolicies_SeparateReplicatedAndLocalContracts()
    {
        Type[] replicatedContracts =
        {
            typeof(IPlayerNetworkService),
            typeof(IReplicatedPlayerStateService),
            typeof(IEnemyAttackReceiver)
        };
        Type[] localContracts =
        {
            typeof(ILocalPlayerInputService),
            typeof(ILocalPlayerCameraService),
            typeof(ILocalPlayerPresentationService)
        };

        AssertExactPolicy(
            replicatedContracts,
            PlayerContractPolicy.AllowedContractCount,
            PlayerContractPolicy.IsAllowed);
        AssertExactPolicy(
            localContracts,
            LocalPlayerContractPolicy.AllowedContractCount,
            LocalPlayerContractPolicy.IsAllowed);

        for (int i = 0; i < localContracts.Length; i++)
            Assert.That(PlayerContractPolicy.IsAllowed(localContracts[i]), Is.False);

        for (int i = 0; i < replicatedContracts.Length; i++)
            Assert.That(LocalPlayerContractPolicy.IsAllowed(replicatedContracts[i]), Is.False);

        Assert.That(PlayerContractPolicy.IsAllowed(typeof(IChatReadService)), Is.False);
        Assert.That(LocalPlayerContractPolicy.IsAllowed(typeof(IPauseService)), Is.False);
    }

    [Test]
    public void CreateChild_RequiresExplicitPolicy()
    {
        using ServiceScope root = new("Root");

        Assert.Throws<ArgumentNullException>(() => root.CreateChild("Child", null));
        Assert.That(root.ChildScopeCount, Is.Zero);
    }

    [Test]
    public void PolicyFailure_RollsBackEarlierTransactionRegistrations()
    {
        using ServiceScope root = new("Root");
        using ServiceScope session = root.CreateChild(
            "Session",
            SessionContractPolicy.Instance);
        using ServiceRegistrationTransaction transaction =
            session.BeginRegistrationTransaction();
        CrossScopeService service = new();

        session.Register<IChatCommandService>(service);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            session.Register<IPauseService>(service));
        Assert.That(failure.Message, Does.Contain(nameof(IPauseService)));
        Assert.That(failure.Message, Does.Contain("Session"));

        transaction.Rollback();

        Assert.That(session.LocalServiceCount, Is.Zero);
        Assert.That(session.TryResolve(out IChatCommandService _), Is.False);
    }

    [Test]
    public void DynamicContractPolicy_RequiresChatForLobbyAndMatchFlowForGame()
    {
        using ServiceScope global = new("Global");
        using ServiceScope session = global.CreateChild(
            "Session",
            SessionContractPolicy.Instance);
        DynamicSessionService service = new();

        Assert.That(
            ProjectSceneDynamicContractPolicy.Validate(
                ProjectSceneKind.Lobby,
                session,
                out string lobbyError),
            Is.False);
        Assert.That(lobbyError, Does.Contain(nameof(IChatReadService)));
        Assert.That(lobbyError, Does.Contain(nameof(IChatCommandService)));

        session.Register<IChatReadService>(service);
        session.Register<IChatCommandService>(service);

        Assert.That(
            ProjectSceneDynamicContractPolicy.Validate(
                ProjectSceneKind.Lobby,
                session,
                out lobbyError),
            Is.True,
            lobbyError);
        Assert.That(
            ProjectSceneDynamicContractPolicy.Validate(
                ProjectSceneKind.Game,
                session,
                out string gameError),
            Is.False);
        Assert.That(gameError, Does.Contain(nameof(IMatchCompletionService)));

        session.Register<IMatchCompletionService>(service);

        Assert.That(
            ProjectSceneDynamicContractPolicy.Validate(
                ProjectSceneKind.Game,
                session,
                out gameError),
            Is.True,
            gameError);
    }

    [Test]
    public void DynamicContractPolicy_DoesNotRequireSessionForMainMenu()
    {
        Assert.That(
            ProjectSceneDynamicContractPolicy.Validate(
                ProjectSceneKind.MainMenu,
                null,
                out string error),
            Is.True,
            error);
    }

    [Test]
    public void SceneActionResult_FinalizesRollbackExactlyOnce()
    {
        int rollbackCount = 0;
        ProjectSceneActionResult rolledBack = ProjectSceneActionResult.Success(
            () => rollbackCount++);

        rolledBack.Rollback();
        rolledBack.Rollback();
        rolledBack.Commit();

        Assert.That(rollbackCount, Is.EqualTo(1));

        ProjectSceneActionResult committed = ProjectSceneActionResult.Success(
            () => rollbackCount++);
        committed.Commit();
        committed.Rollback();

        Assert.That(rollbackCount, Is.EqualTo(1));
    }

    [Test]
    public void SceneActionBatch_RollsBackExecutedActionsInReverseOrder()
    {
        List<string> rollbackOrder = new();
        ProjectSceneActionBatch batch = new();

        batch.Add(ProjectSceneActionResult.Success(
            () => rollbackOrder.Add("first")));
        batch.Add(ProjectSceneActionResult.Failure(
            "second failed",
            rollback: () => rollbackOrder.Add("second")));
        batch.Fail("second failed");
        batch.Rollback();

        CollectionAssert.AreEqual(
            new[] { "second", "first" },
            rollbackOrder);
    }

    private static void AssertExactPolicy(
        Type[] expectedContracts,
        int actualCount,
        Func<Type, bool> isAllowed)
    {
        Assert.That(actualCount, Is.EqualTo(expectedContracts.Length));

        for (int i = 0; i < expectedContracts.Length; i++)
        {
            Assert.That(
                isAllowed.Invoke(expectedContracts[i]),
                Is.True,
                $"{expectedContracts[i].Name} must be allowed by its owner policy.");
        }
    }
}
