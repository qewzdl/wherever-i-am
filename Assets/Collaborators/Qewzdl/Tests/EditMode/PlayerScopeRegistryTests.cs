using System;
using NUnit.Framework;

public sealed class PlayerScopeRegistryTests
{
    private interface ISessionMarker
    {
    }

    private interface IReplicatedMarker
    {
    }

    private interface ILocalMarker
    {
    }

    private sealed class SessionMarker : ISessionMarker
    {
    }

    private sealed class ReplicatedMarker : IReplicatedMarker
    {
    }

    private sealed class LocalMarker : ILocalMarker
    {
    }

    [Test]
    public void TryOpen_CreatesPlayerChildAndIsolatesLocalServices()
    {
        using ServiceScope sessionScope = new("Session");
        SessionMarker sessionMarker = new();
        sessionScope.Register<ISessionMarker>(sessionMarker);
        using PlayerScopeRegistry registry = new(sessionScope);
        ReplicatedMarker replicated = new();
        LocalMarker local = new();
        IPlayerScope openedScope = null;
        IPlayerScope closingScope = null;
        bool servicesActiveWhileClosing = false;
        registry.PlayerScopeOpened += scope => openedScope = scope;
        registry.PlayerScopeClosing += scope =>
        {
            closingScope = scope;
            servicesActiveWhileClosing =
                scope.Services.Resolve<IReplicatedMarker>() == replicated &&
                scope.LocalServices.Resolve<ILocalMarker>() == local;
        };

        Assert.That(
            registry.TryOpen(
                42,
                7,
                true,
                registrar => registrar.Register<IReplicatedMarker>(replicated),
                registrar => registrar.Register<ILocalMarker>(local),
                out PlayerScopeRegistration registration,
                out Exception failure),
            Is.True);

        Assert.That(failure, Is.Null);
        Assert.That(registry.Count, Is.EqualTo(1));
        Assert.That(registry.TryGetPlayerScope(42, out IPlayerScope playerScope), Is.True);
        Assert.That(registry.TryGetLocalPlayerScope(out IPlayerScope localPlayerScope), Is.True);
        Assert.That(openedScope, Is.SameAs(playerScope));
        Assert.That(localPlayerScope, Is.SameAs(playerScope));
        Assert.That(playerScope.NetworkObjectId, Is.EqualTo(42));
        Assert.That(playerScope.OwnerClientId, Is.EqualTo(7));
        Assert.That(playerScope.IsLocalPlayer, Is.True);
        Assert.That(playerScope.Services.Resolve<IReplicatedMarker>(), Is.SameAs(replicated));
        Assert.That(playerScope.Services.Resolve<ISessionMarker>(), Is.SameAs(sessionMarker));
        Assert.That(playerScope.Services.TryResolve(out ILocalMarker _), Is.False);
        Assert.That(playerScope.LocalServices.Resolve<ILocalMarker>(), Is.SameAs(local));
        Assert.That(playerScope.LocalServices.Resolve<IReplicatedMarker>(), Is.SameAs(replicated));

        registration.Dispose();

        Assert.That(closingScope, Is.SameAs(playerScope));
        Assert.That(servicesActiveWhileClosing, Is.True);
        Assert.That(playerScope.IsDisposed, Is.True);
        Assert.That(playerScope.Services, Is.Null);
        Assert.That(playerScope.LocalServices, Is.Null);
        Assert.That(registry.Count, Is.Zero);
        Assert.That(sessionScope.ChildScopeCount, Is.Zero);
    }

    [Test]
    public void TryOpen_RemotePlayerDoesNotCreateLocalScope()
    {
        using ServiceScope sessionScope = new("Session");
        using PlayerScopeRegistry registry = new(sessionScope);
        bool localRegistrationInvoked = false;

        Assert.That(
            registry.TryOpen(
                84,
                12,
                false,
                registrar => registrar.Register<IReplicatedMarker>(new ReplicatedMarker()),
                registrar =>
                {
                    localRegistrationInvoked = true;
                    registrar.Register<ILocalMarker>(new LocalMarker());
                },
                out PlayerScopeRegistration registration,
                out _),
            Is.True);

        Assert.That(localRegistrationInvoked, Is.False);
        Assert.That(registry.TryGetPlayerScope(84, out IPlayerScope playerScope), Is.True);
        Assert.That(playerScope.IsLocalPlayer, Is.False);
        Assert.That(playerScope.LocalServices, Is.Null);
        Assert.That(registry.TryGetLocalPlayerScope(out _), Is.False);

        registration.Dispose();
    }

    [Test]
    public void TryOpen_DuplicateNetworkObjectIdKeepsOriginalScope()
    {
        using ServiceScope sessionScope = new("Session");
        using PlayerScopeRegistry registry = new(sessionScope);
        ReplicatedMarker original = new();
        Assert.That(
            registry.TryOpen(
                128,
                1,
                false,
                registrar => registrar.Register<IReplicatedMarker>(original),
                null,
                out PlayerScopeRegistration originalRegistration,
                out _),
            Is.True);

        Assert.That(
            registry.TryOpen(
                128,
                2,
                false,
                registrar => registrar.Register<IReplicatedMarker>(new ReplicatedMarker()),
                null,
                out PlayerScopeRegistration duplicateRegistration,
                out Exception failure),
            Is.False);

        Assert.That(duplicateRegistration, Is.Null);
        Assert.That(failure, Is.TypeOf<InvalidOperationException>());
        Assert.That(registry.Count, Is.EqualTo(1));
        Assert.That(registry.TryGetPlayerScope(128, out IPlayerScope playerScope), Is.True);
        Assert.That(playerScope.OwnerClientId, Is.EqualTo(1));
        Assert.That(playerScope.Services.Resolve<IReplicatedMarker>(), Is.SameAs(original));

        originalRegistration.Dispose();
    }

    [Test]
    public void TryOpen_LocalRegistrationFailureRollsBackEntirePlayerScope()
    {
        using ServiceScope sessionScope = new("Session");
        using PlayerScopeRegistry registry = new(sessionScope);

        Assert.That(
            registry.TryOpen(
                256,
                3,
                true,
                registrar => registrar.Register<IReplicatedMarker>(new ReplicatedMarker()),
                registrar =>
                {
                    registrar.Register<ILocalMarker>(new LocalMarker());
                    throw new InvalidOperationException("Local registration failed.");
                },
                out PlayerScopeRegistration registration,
                out Exception failure),
            Is.False);

        Assert.That(registration, Is.Null);
        Assert.That(failure, Is.TypeOf<InvalidOperationException>());
        Assert.That(registry.Count, Is.Zero);
        Assert.That(registry.TryGetPlayerScope(256, out _), Is.False);
        Assert.That(registry.TryGetLocalPlayerScope(out _), Is.False);
        Assert.That(sessionScope.ChildScopeCount, Is.Zero);
    }

    [Test]
    public void CloseAll_ClosesEveryPlayerInReverseCreationOrder()
    {
        using ServiceScope sessionScope = new("Session");
        using PlayerScopeRegistry registry = new(sessionScope);
        Assert.That(
            registry.TryOpen(
                1,
                10,
                false,
                registrar => registrar.Register<IReplicatedMarker>(new ReplicatedMarker()),
                null,
                out PlayerScopeRegistration firstRegistration,
                out _),
            Is.True);
        Assert.That(
            registry.TryOpen(
                2,
                20,
                false,
                registrar => registrar.Register<IReplicatedMarker>(new ReplicatedMarker()),
                null,
                out PlayerScopeRegistration secondRegistration,
                out _),
            Is.True);
        string closingOrder = string.Empty;
        registry.PlayerScopeClosing += scope => closingOrder += scope.NetworkObjectId;

        Assert.That(registry.CloseAll(), Is.EqualTo(2));

        Assert.That(closingOrder, Is.EqualTo("21"));
        Assert.That(registry.Count, Is.Zero);
        Assert.That(sessionScope.ChildScopeCount, Is.Zero);
        firstRegistration.Dispose();
        secondRegistration.Dispose();
    }
}
