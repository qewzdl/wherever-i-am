using System;
using NUnit.Framework;
using UnityEngine;

public sealed class SessionScopeControllerTests
{
    private interface IGlobalMarker
    {
    }

    private sealed class GlobalMarker : IGlobalMarker
    {
    }

    private sealed class DynamicService : IChatReadService, IChatCommandService
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
        public ChatChannel CurrentChannel => default;
        public int MessageCount => 0;

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
    }

    private sealed class ReplicatedPlayerServiceStub : IReplicatedPlayerStateService
    {
        public bool IsCrouching => false;
    }

    private sealed class PauseServiceStub : IPauseService
    {
        public bool IsPaused => false;

        public event Action<bool> PauseStateChanged
        {
            add { }
            remove { }
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

    private sealed class MatchCompletionServiceStub : IMatchCompletionService
    {
        public bool IsMatchRunning => true;

        public bool CompleteMatchServerOnly(GameResultData matchResult, string reason)
        {
            return matchResult.HasResult;
        }
    }

    private sealed class GameMapSessionServiceStub : IGameMapSessionService
    {
        public IGameMapCatalog Catalog => null;
        public GameMapDefinition SelectedMap => null;
        public GameMapDefinition ActiveMap => null;
        public GameMapRoot ActiveMapRoot => null;
        public bool IsReadyForMatch => false;
        public EnemyConfig SelectedEnemyConfig => null;

        public event Action MapReady
        {
            add { }
            remove { }
        }

        public bool SelectMap(int mapId)
        {
            return false;
        }

        public bool SelectDifficulty(int difficultyId)
        {
            return false;
        }

        public bool TryGetPlayerSpawn(
            ulong clientId,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = default;
            rotation = default;
            return false;
        }
    }

    private sealed class GameplayNoiseServiceStub : IGameplayNoiseService
    {
        public bool IsInitialized => false;
        public bool IsConfigured => false;

        public bool TryRaiseNoiseServer(
            Vector3 position,
            float radius,
            float loudness,
            GameplayNoiseSourceType sourceType,
            ulong sourceNetworkObjectId = GameplayNoiseEvent.NoNetworkObjectId,
            ulong sourceClientId = GameplayNoiseEvent.NoClientId,
            UnityEngine.Object sourceObject = null)
        {
            return false;
        }

        public bool TryRegisterNoiseServer(GameplayNoiseEvent noiseEvent)
        {
            return false;
        }

        public bool TryFindBestNoise(
            Vector3 listenerPosition,
            float hearingRadius,
            float hearingSensitivity,
            float memoryDuration,
            float minimumLoudness,
            out GameplayNoiseEvent bestNoise,
            out float bestScore)
        {
            bestNoise = default;
            bestScore = default;
            return false;
        }

        public void Clear()
        {
        }
    }

    [Test]
    public void TryOpen_CreatesChildAndRegistersSessionContracts()
    {
        using ServiceScope globalScope = new("Global");
        GameMapSessionServiceStub maps = new();
        GameplayNoiseServiceStub gameplayNoise = new();
        GlobalMarker marker = new();
        globalScope.Register<IGlobalMarker>(marker);
        using SessionScopeController controller = new(
            globalScope,
            maps,
            gameplayNoise);

        Assert.That(controller.TryOpen(out Exception failure), Is.True);
        Assert.That(failure, Is.Null);
        Assert.That(controller.IsOpen, Is.True);
        Assert.That(controller.Services.Resolve<IGameMapSessionService>(), Is.SameAs(maps));
        Assert.That(controller.Services.Resolve<IGameplayNoiseService>(), Is.SameAs(gameplayNoise));
        Assert.That(controller.Services.Resolve<ISessionServiceRegistry>(), Is.SameAs(controller.Services));
        Assert.That(controller.Services.Resolve<IPlayerScopeRegistry>(), Is.Not.Null);
        Assert.That(controller.Services.Resolve<IGlobalMarker>(), Is.SameAs(marker));
        Assert.That(globalScope.ChildScopeCount, Is.EqualTo(1));
    }

    [Test]
    public void DynamicRegistration_CommitsBatchAndUnregistersHandlesInOneChange()
    {
        using ServiceScope globalScope = new("Global");
        using SessionScopeController controller = CreateController(globalScope);
        Assert.That(controller.TryOpen(out _), Is.True);
        Assert.That(controller.TryGetRegistry(out ISessionServiceRegistry registry), Is.True);
        DynamicService service = new();
        int changeCount = 0;
        registry.ServicesChanged += () => changeCount++;

        Assert.That(
            controller.TryRegisterServices(
                registrar =>
                {
                    registrar.Register<IChatReadService>(service);
                    registrar.Register<IChatCommandService>(service);
                },
                out SessionServiceRegistration registrations,
                out Exception failure),
            Is.True);

        Assert.That(failure, Is.Null);
        Assert.That(changeCount, Is.EqualTo(1));
        Assert.That(registry.Resolve<IChatReadService>(), Is.SameAs(service));
        Assert.That(registry.Resolve<IChatCommandService>(), Is.SameAs(service));

        registrations.Dispose();

        Assert.That(changeCount, Is.EqualTo(2));
        Assert.That(registry.TryResolve(out IChatReadService _), Is.False);
        Assert.That(registry.TryResolve(out IChatCommandService _), Is.False);
    }

    [Test]
    public void DynamicRegistration_RollsBackWholeBatchOnDuplicateContract()
    {
        using ServiceScope globalScope = new("Global");
        using SessionScopeController controller = CreateController(globalScope);
        Assert.That(controller.TryOpen(out _), Is.True);
        Assert.That(controller.TryGetRegistry(out ISessionServiceRegistry registry), Is.True);
        DynamicService original = new();
        DynamicService duplicate = new();
        Assert.That(
            controller.TryRegisterServices(
                registrar => registrar.Register<IChatReadService>(original),
                out SessionServiceRegistration originalRegistration,
                out _),
            Is.True);

        int changeCount = 0;
        registry.ServicesChanged += () => changeCount++;

        Assert.That(
            controller.TryRegisterServices(
                registrar =>
                {
                    registrar.Register<IChatCommandService>(duplicate);
                    registrar.Register<IChatReadService>(duplicate);
                },
                out SessionServiceRegistration failedRegistrations,
                out Exception failure),
            Is.False);

        Assert.That(failedRegistrations, Is.Null);
        Assert.That(failure, Is.TypeOf<InvalidOperationException>());
        Assert.That(changeCount, Is.Zero);
        Assert.That(registry.Resolve<IChatReadService>(), Is.SameAs(original));
        Assert.That(registry.TryResolve(out IChatCommandService _), Is.False);

        originalRegistration.Dispose();
    }

    [Test]
    public void DynamicRegistration_RejectsNonSessionContractWithoutMutation()
    {
        using ServiceScope globalScope = new("Global");
        using SessionScopeController controller = CreateController(globalScope);
        Assert.That(controller.TryOpen(out _), Is.True);
        Assert.That(controller.TryGetRegistry(out ISessionServiceRegistry registry), Is.True);

        Assert.That(
            controller.TryRegisterServices(
                registrar => registrar.Register<IPauseService>(new PauseServiceStub()),
                out SessionServiceRegistration registrations,
                out Exception failure),
            Is.False);

        Assert.That(registrations, Is.Null);
        Assert.That(failure, Is.TypeOf<InvalidOperationException>());
        Assert.That(registry.TryResolve(out IPauseService _), Is.False);
    }

    [Test]
    public void DynamicRegistrationChurn_DoesNotAccumulateOrderEntries()
    {
        const int RegistrationCount = 10_000;

        using ServiceScope globalScope = new("Global");
        using SessionScopeController controller = CreateController(globalScope);
        Assert.That(controller.TryOpen(out _), Is.True);
        Assert.That(controller.TryGetScope(out ServiceScope sessionScope), Is.True);
        int baselineOrderCount = sessionScope.RegistrationOrderCount;
        int baselineServiceCount = sessionScope.LocalServiceCount;
        MatchCompletionServiceStub service = new();

        for (int i = 0; i < RegistrationCount; i++)
        {
            if (!controller.TryRegisterServices(
                    registrar => registrar.Register<IMatchCompletionService>(service),
                    out SessionServiceRegistration registration,
                    out Exception failure))
            {
                Assert.Fail(
                    $"Registration churn failed at iteration {i}: {failure}");
            }

            registration.Dispose();

            if (sessionScope.RegistrationOrderCount != baselineOrderCount)
            {
                Assert.Fail(
                    $"Registration order grew at iteration {i}. " +
                    $"Expected {baselineOrderCount}, got " +
                    $"{sessionScope.RegistrationOrderCount}.");
            }
        }

        Assert.That(sessionScope.LocalServiceCount, Is.EqualTo(baselineServiceCount));
        Assert.That(sessionScope.RegistrationOrderCount, Is.EqualTo(baselineOrderCount));
        Assert.That(sessionScope.TryResolve(out IMatchCompletionService _), Is.False);
    }

    [Test]
    public void TryOpen_RollsBackPartiallyRegisteredChildWhenRegistrationFails()
    {
        using ServiceScope globalScope = new("Global");
        using SessionScopeController controller = new(
            globalScope,
            new GameMapSessionServiceStub(),
            null);

        Assert.That(controller.TryOpen(out Exception failure), Is.False);
        Assert.That(failure, Is.TypeOf<ArgumentNullException>());
        Assert.That(controller.IsOpen, Is.False);
        Assert.That(controller.Services, Is.Null);
        Assert.That(globalScope.ChildScopeCount, Is.Zero);
    }

    [Test]
    public void TryOpen_RejectsSecondOpenWithoutReplacingActiveScope()
    {
        using ServiceScope globalScope = new("Global");
        using SessionScopeController controller = CreateController(globalScope);

        Assert.That(controller.TryOpen(out _), Is.True);
        IServiceResolver firstResolver = controller.Services;

        Assert.That(controller.TryOpen(out Exception failure), Is.False);
        Assert.That(failure, Is.TypeOf<InvalidOperationException>());
        Assert.That(controller.Services, Is.SameAs(firstResolver));
        Assert.That(globalScope.ChildScopeCount, Is.EqualTo(1));
    }

    [Test]
    public void Close_DisposesResolverAndAllowsFollowingSession()
    {
        using ServiceScope globalScope = new("Global");
        using SessionScopeController controller = CreateController(globalScope);
        Assert.That(controller.TryOpen(out _), Is.True);
        IServiceResolver firstResolver = controller.Services;

        Assert.That(controller.Close(), Is.True);
        Assert.That(controller.Close(), Is.False);
        Assert.That(controller.IsOpen, Is.False);
        Assert.That(controller.Services, Is.Null);
        Assert.That(globalScope.ChildScopeCount, Is.Zero);
        Assert.Throws<ObjectDisposedException>(() =>
            firstResolver.Resolve<IGameMapSessionService>());

        Assert.That(controller.TryOpen(out Exception failure), Is.True);
        Assert.That(failure, Is.Null);
        Assert.That(controller.Services, Is.Not.SameAs(firstResolver));
        Assert.That(globalScope.ChildScopeCount, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_ClosesScopeExactlyOnceAndPreventsReopen()
    {
        using ServiceScope globalScope = new("Global");
        SessionScopeController controller = CreateController(globalScope);
        Assert.That(controller.TryOpen(out _), Is.True);

        controller.Dispose();
        controller.Dispose();

        Assert.That(globalScope.ChildScopeCount, Is.Zero);
        Assert.That(controller.IsOpen, Is.False);
        Assert.That(controller.TryOpen(out Exception failure), Is.False);
        Assert.That(failure, Is.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void GlobalDispose_InvalidatesSessionResolver()
    {
        ServiceScope globalScope = new("Global");
        using SessionScopeController controller = CreateController(globalScope);
        Assert.That(controller.TryOpen(out _), Is.True);
        IServiceResolver resolver = controller.Services;

        globalScope.Dispose();

        Assert.That(controller.IsOpen, Is.False);
        Assert.That(controller.Services, Is.Null);
        Assert.Throws<ObjectDisposedException>(() =>
            resolver.Resolve<IGameMapSessionService>());
    }

    [Test]
    public void Close_ClosesPlayerScopesBeforeSessionResolver()
    {
        using ServiceScope globalScope = new("Global");
        using SessionScopeController controller = CreateController(globalScope);
        Assert.That(controller.TryOpen(out _), Is.True);
        IServiceResolver sessionResolver = controller.Services;
        IPlayerScopeRegistry playerRegistry =
            sessionResolver.Resolve<IPlayerScopeRegistry>();
        bool sessionWasActiveWhilePlayerClosed = false;
        playerRegistry.PlayerScopeClosing += scope =>
        {
            sessionWasActiveWhilePlayerClosed =
                sessionResolver.Resolve<IGameMapSessionService>() != null &&
                scope.Services != null;
        };
        Assert.That(
            controller.TryOpenPlayerScope(
                512,
                24,
                false,
                registrar => registrar.Register<IReplicatedPlayerStateService>(
                    new ReplicatedPlayerServiceStub()),
                null,
                out PlayerScopeRegistration registration,
                out _),
            Is.True);
        Assert.That(playerRegistry.TryGetPlayerScope(512, out IPlayerScope playerScope), Is.True);

        Assert.That(controller.Close(), Is.True);

        Assert.That(sessionWasActiveWhilePlayerClosed, Is.True);
        Assert.That(playerScope.IsDisposed, Is.True);
        Assert.Throws<ObjectDisposedException>(() =>
            sessionResolver.Resolve<IGameMapSessionService>());
        registration.Dispose();
    }

    private static SessionScopeController CreateController(ServiceScope globalScope)
    {
        return new SessionScopeController(
            globalScope,
            new GameMapSessionServiceStub(),
            new GameplayNoiseServiceStub());
    }
}
