using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

public sealed class ServiceScopeTests
{
    private interface IFirstService
    {
        string Id { get; }
    }

    private interface ISecondService
    {
        string Id { get; }
    }

    private sealed class PlainService : IFirstService, ISecondService
    {
        public PlainService(string id)
        {
            Id = id;
        }

        public string Id { get; }
    }

    private sealed class DisposableService : IFirstService, ISecondService, IDisposable
    {
        private readonly IList<string> disposeOrder;

        public DisposableService(string id, IList<string> order = null)
        {
            Id = id;
            disposeOrder = order;
        }

        public string Id { get; }
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            disposeOrder?.Add(Id);
        }
    }

    [Test]
    public void ScopeInfrastructure_ExposesOnlyReadOnlyResolverBoundaries()
    {
        Type[] internalTypes =
        {
            typeof(ServiceScope),
            typeof(ServiceRegistration),
            typeof(ServiceRegistrationTransaction),
            typeof(ServiceRegistrationOwnership),
            typeof(ServiceShadowingPolicy),
            typeof(IServiceRegistrationPolicy),
            typeof(GlobalServiceContractPolicy),
            typeof(GlobalServiceDiagnostics),
            typeof(GlobalServicePublicationState),
            typeof(GlobalServicePublication),
            typeof(ProjectRuntimeLifecycleState),
            typeof(SceneRuntimeScope),
            typeof(SceneRuntimeScopeRegistry),
            typeof(ISceneServiceRegistrar),
            typeof(ISessionServiceRegistry),
            typeof(INetworkConnectionService)
        };

        for (int i = 0; i < internalTypes.Length; i++)
        {
            Assert.That(
                internalTypes[i].IsVisible,
                Is.False,
                $"{internalTypes[i].Name} must remain assembly-internal.");
        }

        Assert.That(typeof(IServiceResolver).IsVisible, Is.True);
        Assert.That(typeof(G).IsVisible, Is.True);
        Assert.That(typeof(IPlayerScope).IsVisible, Is.True);
        Assert.That(typeof(IPlayerScopeRegistry).IsVisible, Is.True);
        Assert.That(typeof(NetworkObjectServiceContext).IsVisible, Is.True);
        Assert.That(
            typeof(SceneFeatureContext)
                .GetProperty(nameof(SceneFeatureContext.Services))
                ?.GetMethod
                ?.IsPublic,
            Is.True);

        AssertAssemblyInternalGetter(typeof(ProjectContext), "Services");
        AssertAssemblyInternalGetter(typeof(SceneFeatureContext), "Registrar");
        AssertAssemblyInternalGetter(typeof(NetworkSessionOrchestrator), "SessionServices");
        AssertAssemblyInternalGetter(typeof(NetworkSessionFlowService), "SessionServices");
        AssertAssemblyInternalGetter(typeof(NetworkSessionShutdownCoordinator), "SessionServices");
        Assert.That(
            typeof(G).GetProperty(nameof(G.IsReady))?.GetMethod?.IsPublic,
            Is.True);
        Assert.That(
            typeof(G).GetMethod(nameof(G.Resolve), BindingFlags.Public | BindingFlags.Static),
            Is.Not.Null);
        Assert.That(
            typeof(G).GetMethod(nameof(G.TryResolve), BindingFlags.Public | BindingFlags.Static),
            Is.Not.Null);
        Assert.That(
            typeof(G).GetProperty("Services", BindingFlags.Public | BindingFlags.Static),
            Is.Null);
        Assert.That(
            typeof(G).GetMethod("Register", BindingFlags.Public | BindingFlags.Static),
            Is.Null);
    }

    [Test]
    public void ProjectContext_DeclaresNoPublicCompositionOrLifecycleApi()
    {
        PropertyInfo[] publicProperties = typeof(ProjectContext).GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        MethodInfo[] publicMethods = typeof(ProjectContext).GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.That(publicProperties, Is.Empty);
        Assert.That(publicMethods, Is.Empty);
        Assert.That(
            typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(typeof(ProjectContext)),
            Is.True);
        Assert.That(typeof(IDisposable).IsAssignableFrom(typeof(ProjectContext)), Is.False);
    }

    [Test]
    public void Register_RejectsConcreteContract()
    {
        using ServiceScope scope = new("Root");

        Assert.Throws<ArgumentException>(() => scope.Register(new PlainService("service")));
    }

    [Test]
    public void Resolve_UsesParentWhenLocalContractIsMissing()
    {
        using ServiceScope root = new("Global");
        ServiceScope child = root.CreateChild("Session");
        PlainService expected = new("global");
        root.Register<IFirstService>(expected);

        Assert.That(child.Resolve<IFirstService>(), Is.SameAs(expected));
        Assert.That(child.TryResolve(out IFirstService resolved), Is.True);
        Assert.That(resolved, Is.SameAs(expected));
    }

    [Test]
    public void Register_RejectsDuplicateContractInsideSameScope()
    {
        using ServiceScope scope = new("Root");
        scope.Register<IFirstService>(new PlainService("first"));

        Assert.Throws<InvalidOperationException>(() =>
            scope.Register<IFirstService>(
                new PlainService("second"),
                shadowing: ServiceShadowingPolicy.Allow));
    }

    [Test]
    public void Register_RejectsParentShadowingByDefault()
    {
        using ServiceScope root = new("Global");
        ServiceScope child = root.CreateChild("Session");
        root.Register<IFirstService>(new PlainService("global"));

        Assert.Throws<InvalidOperationException>(() =>
            child.Register<IFirstService>(new PlainService("session")));
    }

    [Test]
    public void Register_AllowsExplicitParentShadowingAndUsesLocalService()
    {
        using ServiceScope root = new("Global");
        ServiceScope child = root.CreateChild("Session");
        PlainService global = new("global");
        PlainService session = new("session");
        root.Register<IFirstService>(global);

        child.Register<IFirstService>(
            session,
            shadowing: ServiceShadowingPolicy.Allow);

        Assert.That(root.Resolve<IFirstService>(), Is.SameAs(global));
        Assert.That(child.Resolve<IFirstService>(), Is.SameAs(session));
    }

    [Test]
    public void Dispose_CleansScopeOwnedServicesInReverseRegistrationOrder()
    {
        List<string> order = new();
        ServiceScope scope = new("Root");
        DisposableService first = new("first", order);
        DisposableService second = new("second", order);
        ServiceRegistration firstRegistration = scope.Register<IFirstService>(
            first,
            ServiceRegistrationOwnership.ScopeOwned);
        ServiceRegistration secondRegistration = scope.Register<ISecondService>(
            second,
            ServiceRegistrationOwnership.ScopeOwned);

        scope.Dispose();

        CollectionAssert.AreEqual(new[] { "second", "first" }, order);
        Assert.That(first.DisposeCount, Is.EqualTo(1));
        Assert.That(second.DisposeCount, Is.EqualTo(1));
        Assert.That(firstRegistration.IsActive, Is.False);
        Assert.That(secondRegistration.IsActive, Is.False);
        Assert.That(scope.LocalServiceCount, Is.Zero);
    }

    [Test]
    public void Dispose_DoesNotDisposeUnityOwnedService()
    {
        ServiceScope scope = new("Root");
        DisposableService service = new("unity-owned");
        scope.Register<IFirstService>(service, ServiceRegistrationOwnership.UnityOwned);

        scope.Dispose();

        Assert.That(service.DisposeCount, Is.Zero);
    }

    [Test]
    public void ScopeOwnedService_RequiresDisposeOrExplicitCleanup()
    {
        using ServiceScope scope = new("Root");

        Assert.Throws<ArgumentException>(() =>
            scope.Register<IFirstService>(
                new PlainService("service"),
                ServiceRegistrationOwnership.ScopeOwned));
    }

    [Test]
    public void RegistrationHandle_InvokesExplicitCleanupExactlyOnce()
    {
        ServiceScope scope = new("Root");
        PlainService service = new("service");
        int cleanupCount = 0;
        ServiceRegistration registration = scope.Register<IFirstService>(
            service,
            ServiceRegistrationOwnership.ScopeOwned,
            cleanup: _ => cleanupCount++);

        registration.Dispose();
        registration.Dispose();
        scope.Dispose();

        Assert.That(cleanupCount, Is.EqualTo(1));
        Assert.That(registration.IsActive, Is.False);
    }

    [Test]
    public void DisposedScope_RejectsResolutionAndMutation()
    {
        ServiceScope scope = new("Root");
        scope.Dispose();

        Assert.Throws<ObjectDisposedException>(() => scope.Resolve<IFirstService>());
        Assert.Throws<ObjectDisposedException>(() => scope.TryResolve<IFirstService>(out _));
        Assert.Throws<ObjectDisposedException>(() =>
            scope.Register<IFirstService>(new PlainService("service")));
        Assert.Throws<ObjectDisposedException>(() => scope.CreateChild("Child"));
        Assert.Throws<ObjectDisposedException>(() => scope.BeginRegistrationTransaction());
        Assert.DoesNotThrow(scope.Dispose);
    }

    [Test]
    public void Transaction_RollsBackPartialRegistrationInReverseOrder()
    {
        using ServiceScope scope = new("Root");
        List<string> order = new();

        using (scope.BeginRegistrationTransaction())
        {
            scope.Register<IFirstService>(
                new DisposableService("first", order),
                ServiceRegistrationOwnership.ScopeOwned);
            scope.Register<ISecondService>(
                new DisposableService("second", order),
                ServiceRegistrationOwnership.ScopeOwned);

            Assert.Throws<InvalidOperationException>(() =>
                scope.Register<IFirstService>(new PlainService("duplicate")));
        }

        Assert.That(scope.TryResolve<IFirstService>(out _), Is.False);
        Assert.That(scope.TryResolve<ISecondService>(out _), Is.False);
        CollectionAssert.AreEqual(new[] { "second", "first" }, order);
    }

    [Test]
    public void Transaction_CommitKeepsRegistrations()
    {
        using ServiceScope scope = new("Root");
        PlainService expected = new("service");

        using (ServiceRegistrationTransaction transaction = scope.BeginRegistrationTransaction())
        {
            scope.Register<IFirstService>(expected);
            transaction.Commit();
        }

        Assert.That(scope.Resolve<IFirstService>(), Is.SameAs(expected));
    }

    [Test]
    public void Dispose_ClosesChildrenBeforeParentServices()
    {
        List<string> order = new();
        ServiceScope root = new("Global");
        ServiceScope child = root.CreateChild("Session");
        root.Register<IFirstService>(
            new DisposableService("global", order),
            ServiceRegistrationOwnership.ScopeOwned);
        child.Register<ISecondService>(
            new DisposableService("session", order),
            ServiceRegistrationOwnership.ScopeOwned);

        root.Dispose();

        CollectionAssert.AreEqual(new[] { "session", "global" }, order);
        Assert.That(child.IsDisposed, Is.True);
        Assert.Throws<ObjectDisposedException>(() => child.Resolve<ISecondService>());
    }

    [Test]
    public void Dispose_ContinuesCleanupAfterExceptions()
    {
        List<string> order = new();
        ServiceScope scope = new("Root");
        scope.Register<IFirstService>(
            new PlainService("first"),
            ServiceRegistrationOwnership.ScopeOwned,
            cleanup: service =>
            {
                order.Add(service.Id);
                throw new InvalidOperationException("first cleanup failed");
            });
        scope.Register<ISecondService>(
            new PlainService("second"),
            ServiceRegistrationOwnership.ScopeOwned,
            cleanup: service =>
            {
                order.Add(service.Id);
                throw new InvalidOperationException("second cleanup failed");
            });

        AggregateException exception = Assert.Throws<AggregateException>(scope.Dispose);

        Assert.That(exception.InnerExceptions.Count, Is.EqualTo(2));
        CollectionAssert.AreEqual(new[] { "second", "first" }, order);
        Assert.That(scope.IsDisposed, Is.True);
    }

    [Test]
    public void ScopeOwnedInstance_CannotHaveTwoOwnersInScopeTree()
    {
        using ServiceScope root = new("Global");
        ServiceScope child = root.CreateChild("Session");
        DisposableService service = new("service");
        root.Register<IFirstService>(service, ServiceRegistrationOwnership.ScopeOwned);

        Assert.Throws<InvalidOperationException>(() =>
            child.Register<ISecondService>(
                service,
                ServiceRegistrationOwnership.ScopeOwned));
    }

    [Test]
    public void ScopeOwnedInstance_CanExposeMultipleContractsAndDisposesOnce()
    {
        ServiceScope scope = new("Root");
        DisposableService service = new("service");
        scope.Register<IFirstService>(service, ServiceRegistrationOwnership.ScopeOwned);
        scope.Register<ISecondService>(service, ServiceRegistrationOwnership.ScopeOwned);

        scope.Dispose();

        Assert.That(service.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void ScopeOwnedInstance_ReusesExplicitCleanupForAdditionalContracts()
    {
        ServiceScope scope = new("Root");
        PlainService service = new("service");
        int cleanupCount = 0;
        scope.Register<IFirstService>(
            service,
            ServiceRegistrationOwnership.ScopeOwned,
            cleanup: _ => cleanupCount++);
        scope.Register<ISecondService>(service, ServiceRegistrationOwnership.ScopeOwned);

        scope.Dispose();

        Assert.That(cleanupCount, Is.EqualTo(1));
    }

    [Test]
    public void SiblingScopes_CanRegisterSameContractIndependently()
    {
        using ServiceScope root = new("Global");
        ServiceScope firstScene = root.CreateChild("Scene 1");
        ServiceScope secondScene = root.CreateChild("Scene 2");
        PlainService first = new("first");
        PlainService second = new("second");
        firstScene.Register<IFirstService>(first);
        secondScene.Register<IFirstService>(second);

        Assert.That(firstScene.Resolve<IFirstService>(), Is.SameAs(first));
        Assert.That(secondScene.Resolve<IFirstService>(), Is.SameAs(second));
    }

    private static void AssertAssemblyInternalGetter(Type ownerType, string propertyName)
    {
        PropertyInfo property = ownerType.GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.That(property, Is.Not.Null, $"{ownerType.Name}.{propertyName} is missing.");
        Assert.That(
            property.GetGetMethod(true)?.IsAssembly,
            Is.True,
            $"{ownerType.Name}.{propertyName} must remain assembly-internal.");
    }
}

public sealed class GTests
{
    private const string DefaultPublicationOwner = "GTests Bootstrap";

    private interface IScopedTestService
    {
    }

    private sealed class GlobalTestService : IUiErrorService
    {
        internal GlobalTestService(string id)
        {
            Id = id;
        }

        public string Id { get; }

        public void ShowError(string message)
        {
        }

        public void HideError()
        {
        }
    }

    private sealed class ScopedTestService : IScopedTestService
    {
    }

    private sealed class ForbiddenPauseService : IPauseService
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

    private sealed class ForbiddenChatService : IChatReadService, IChatCommandService
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

        public bool CanSubmitMessages => false;
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

    private sealed class ForbiddenPlayerService : IReplicatedPlayerStateService
    {
        public bool IsCrouching => false;
    }

    [SetUp]
    public void SetUp()
    {
        G.ResetRuntimeState();
    }

    [TearDown]
    public void TearDown()
    {
        G.ResetRuntimeState();
    }

    [Test]
    public void Resolve_BeforePublicationReportsUnavailableGlobalServices()
    {
        Assert.That(G.IsReady, Is.False);
        Assert.That(G.TryResolve(out IUiErrorService service), Is.False);
        Assert.That(service, Is.Null);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => G.Resolve<IUiErrorService>());

        Assert.That(exception.Message, Does.Contain(nameof(IUiErrorService)));
        Assert.That(exception.Message, Does.Contain("generation=0"));
        Assert.That(
            exception.Message,
            Does.Contain($"state={GlobalServicePublicationState.Unpublished}"));
    }

    [Test]
    public void Publication_ExposesOnlyGlobalResolverUntilHandleIsDisposed()
    {
        using ServiceScope globalScope = CreateGlobalScope();
        ServiceScope sessionScope = globalScope.CreateChild("Session");
        GlobalTestService expected = new("global");
        globalScope.Register<IUiErrorService>(expected);
        sessionScope.Register<IScopedTestService>(new ScopedTestService());

        using (GlobalServicePublication publication =
               G.Publish(globalScope, DefaultPublicationOwner))
        {
            Assert.That(publication.IsActive, Is.True);
            Assert.That(G.IsReady, Is.True);
            Assert.That(G.Resolve<IUiErrorService>(), Is.SameAs(expected));
            Assert.That(G.TryResolve(out IUiErrorService resolved), Is.True);
            Assert.That(resolved, Is.SameAs(expected));
            Assert.Throws<InvalidOperationException>(() =>
                G.TryResolve(out IScopedTestService _));
        }

        Assert.That(G.IsReady, Is.False);
        Assert.That(G.TryResolve(out IUiErrorService _), Is.False);
    }

    [Test]
    public void Publish_RejectsDuplicateWithoutReplacingActiveResolver()
    {
        const string activeOwner = "ProjectContext 'Primary Bootstrap'";
        const string requestedOwner = "ProjectContext 'Duplicate Bootstrap'";
        using ServiceScope firstScope = CreateGlobalScope("First Global");
        using ServiceScope secondScope = CreateGlobalScope("Second Global");
        GlobalTestService first = new("first");
        firstScope.Register<IUiErrorService>(first);
        secondScope.Register<IUiErrorService>(new GlobalTestService("second"));
        using GlobalServicePublication publication = G.Publish(firstScope, activeOwner);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => G.Publish(secondScope, requestedOwner));

        Assert.That(exception.Message, Does.Contain(activeOwner));
        Assert.That(exception.Message, Does.Contain(requestedOwner));
        Assert.That(exception.Message, Does.Contain("generation="));
        Assert.That(
            exception.Message,
            Does.Contain($"state={GlobalServicePublicationState.Ready}"));
        Assert.That(G.Resolve<IUiErrorService>(), Is.SameAs(first));
        Assert.That(publication.IsActive, Is.True);
    }

    [Test]
    public void Publish_RejectsMissingOwnerDescription()
    {
        using ServiceScope globalScope = CreateGlobalScope();

        Assert.Throws<ArgumentException>(() => G.Publish(globalScope, string.Empty));
        Assert.That(G.IsReady, Is.False);
    }

    [Test]
    public void PublicationHandle_DisposeIsIdempotentAndAllowsNextGeneration()
    {
        using ServiceScope globalScope = CreateGlobalScope();
        globalScope.Register<IUiErrorService>(new GlobalTestService("global"));
        GlobalServicePublication publication = G.Publish(
            globalScope,
            DefaultPublicationOwner);

        publication.Dispose();
        publication.Dispose();

        Assert.That(publication.IsActive, Is.False);
        Assert.That(G.IsReady, Is.False);

        using GlobalServicePublication nextPublication = G.Publish(
            globalScope,
            DefaultPublicationOwner);
        Assert.That(nextPublication.IsActive, Is.True);
        Assert.That(G.IsReady, Is.True);
    }

    [Test]
    public void StalePublication_CannotClearNewGenerationAfterRuntimeReset()
    {
        using ServiceScope firstScope = CreateGlobalScope("First Global");
        using ServiceScope secondScope = CreateGlobalScope("Second Global");
        GlobalTestService second = new("second");
        firstScope.Register<IUiErrorService>(new GlobalTestService("first"));
        secondScope.Register<IUiErrorService>(second);
        GlobalServicePublication stalePublication = G.Publish(
            firstScope,
            "Stale Bootstrap");

        G.ResetRuntimeState();

        using GlobalServicePublication currentPublication = G.Publish(
            secondScope,
            "Current Bootstrap");
        stalePublication.Dispose();

        Assert.That(currentPublication.IsActive, Is.True);
        Assert.That(G.IsReady, Is.True);
        Assert.That(G.Resolve<IUiErrorService>(), Is.SameAs(second));
    }

    [Test]
    public void DisposedResolver_IsNeverReportedAsReady()
    {
        ServiceScope globalScope = CreateGlobalScope();
        globalScope.Register<IUiErrorService>(new GlobalTestService("global"));
        using GlobalServicePublication publication = G.Publish(
            globalScope,
            DefaultPublicationOwner);

        globalScope.Dispose();

        Assert.That(G.IsReady, Is.False);
        Assert.That(G.TryResolve(out IUiErrorService service), Is.False);
        Assert.That(service, Is.Null);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => G.Resolve<IUiErrorService>());

        Assert.That(exception.Message, Does.Contain(nameof(IUiErrorService)));
        Assert.That(
            exception.Message,
            Does.Contain($"state={GlobalServicePublicationState.ResolverDisposed}"));
    }

    [Test]
    public void Resolve_RejectsConcreteContract()
    {
        using ServiceScope globalScope = CreateGlobalScope();
        using GlobalServicePublication publication = G.Publish(
            globalScope,
            DefaultPublicationOwner);

        Assert.Throws<ArgumentException>(() => G.Resolve<GlobalTestService>());
        Assert.Throws<ArgumentException>(() =>
            G.TryResolve(out GlobalTestService _));
    }

    [Test]
    public void Diagnostics_TracksGenerationOwnerAndPublicationState()
    {
        GlobalServiceDiagnostics initial = G.Diagnostics;

        Assert.That(initial.Generation, Is.Zero);
        Assert.That(initial.State, Is.EqualTo(GlobalServicePublicationState.Unpublished));
        Assert.That(initial.Owner, Is.Null);

        using ServiceScope globalScope = CreateGlobalScope();
        globalScope.Register<IUiErrorService>(new GlobalTestService("global"));
        GlobalServicePublication publication = G.Publish(
            globalScope,
            DefaultPublicationOwner);

        GlobalServiceDiagnostics ready = G.Diagnostics;

        Assert.That(ready.Generation, Is.GreaterThan(0));
        Assert.That(ready.State, Is.EqualTo(GlobalServicePublicationState.Ready));
        Assert.That(ready.Owner, Is.EqualTo(DefaultPublicationOwner));

        globalScope.Dispose();

        GlobalServiceDiagnostics disposed = G.Diagnostics;

        Assert.That(disposed.Generation, Is.EqualTo(ready.Generation));
        Assert.That(
            disposed.State,
            Is.EqualTo(GlobalServicePublicationState.ResolverDisposed));
        Assert.That(disposed.Owner, Is.EqualTo(DefaultPublicationOwner));

        publication.Dispose();

        GlobalServiceDiagnostics unpublished = G.Diagnostics;

        Assert.That(unpublished.Generation, Is.Zero);
        Assert.That(
            unpublished.State,
            Is.EqualTo(GlobalServicePublicationState.Unpublished));
        Assert.That(unpublished.Owner, Is.Null);
    }

    [Test]
    public void GlobalContractAllowlist_IsExactAndRejectsNonGlobalContracts()
    {
        Type[] expectedContracts =
        {
            typeof(IProjectSceneRegistry),
            typeof(IGameStateService),
            typeof(IProjectSceneFlowService),
            typeof(INetworkSessionService),
            typeof(IUiErrorService),
            typeof(IAudioService),
            typeof(IGameMapCatalog)
        };

        Assert.That(
            GlobalServiceContractPolicy.AllowedContractCount,
            Is.EqualTo(expectedContracts.Length));

        for (int i = 0; i < expectedContracts.Length; i++)
            Assert.That(GlobalServiceContractPolicy.IsAllowed(expectedContracts[i]), Is.True);

        Type[] forbiddenContracts =
        {
            typeof(IPauseService),
            typeof(IChatReadService),
            typeof(IChatCommandService),
            typeof(IReplicatedPlayerStateService),
            typeof(ILocalPlayerInputService),
            typeof(ILocalPlayerCameraService),
            typeof(ILocalPlayerPresentationService),
            typeof(INetworkConnectionService),
            typeof(IScopedTestService)
        };

        for (int i = 0; i < forbiddenContracts.Length; i++)
        {
            Assert.That(
                GlobalServiceContractPolicy.IsAllowed(forbiddenContracts[i]),
                Is.False,
                $"{forbiddenContracts[i].Name} must not be a Global contract.");
        }
    }

    [Test]
    public void GlobalScopePolicy_RejectsSceneSessionAndPlayerRegistrations()
    {
        using ServiceScope globalScope = CreateGlobalScope();
        ForbiddenPauseService pauseService = new();
        ForbiddenChatService chatService = new();
        ForbiddenPlayerService playerService = new();

        Assert.Throws<InvalidOperationException>(() =>
            globalScope.Register<IPauseService>(pauseService));
        Assert.Throws<InvalidOperationException>(() =>
            globalScope.Register<IChatReadService>(chatService));
        Assert.Throws<InvalidOperationException>(() =>
            globalScope.Register<IChatCommandService>(chatService));
        Assert.Throws<InvalidOperationException>(() =>
            globalScope.Register<IReplicatedPlayerStateService>(playerService));

        Assert.That(globalScope.LocalServiceCount, Is.Zero);
    }

    [Test]
    public void GlobalScopePolicy_DoesNotRestrictChildScopes()
    {
        using ServiceScope globalScope = CreateGlobalScope();
        ServiceScope sessionScope = globalScope.CreateChild("Session");
        ServiceScope sceneScope = globalScope.CreateChild("Game Scene");
        ServiceScope playerScope = sessionScope.CreateChild("Player");
        ForbiddenChatService chatService = new();

        Assert.DoesNotThrow(() =>
            sessionScope.Register<IChatReadService>(chatService));
        Assert.DoesNotThrow(() =>
            sceneScope.Register<IPauseService>(new ForbiddenPauseService()));
        Assert.DoesNotThrow(() =>
            playerScope.Register<IReplicatedPlayerStateService>(new ForbiddenPlayerService()));
    }

    private static ServiceScope CreateGlobalScope(string name = "Global")
    {
        return new ServiceScope(name, GlobalServiceContractPolicy.Instance);
    }
}
