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
            typeof(SceneRuntimeScope),
            typeof(SceneRuntimeScopeRegistry),
            typeof(ISceneServiceRegistrar),
            typeof(ISessionServiceRegistry)
        };

        for (int i = 0; i < internalTypes.Length; i++)
        {
            Assert.That(
                internalTypes[i].IsVisible,
                Is.False,
                $"{internalTypes[i].Name} must remain assembly-internal.");
        }

        Assert.That(typeof(IServiceResolver).IsVisible, Is.True);
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
