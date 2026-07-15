using System;
using NUnit.Framework;

public sealed class ChildServiceContractPolicyTests
{
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
