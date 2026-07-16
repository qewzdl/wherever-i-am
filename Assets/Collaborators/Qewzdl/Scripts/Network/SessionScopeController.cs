using System;
using System.Collections.Generic;

internal sealed class SessionScopeController : IDisposable
{
    private readonly ServiceScope globalScope;
    private readonly IGameMapSessionService gameMapService;
    private readonly IGameplayNoiseService gameplayNoiseService;

    private ServiceScope sessionScope;
    private SessionServiceRegistry sessionRegistry;
    private PlayerScopeRegistry playerScopeRegistry;
    private bool disposed;

    internal SessionScopeController(
        ServiceScope parentScope,
        IGameMapSessionService maps,
        IGameplayNoiseService gameplayNoise)
    {
        globalScope = parentScope ?? throw new ArgumentNullException(nameof(parentScope));
        gameMapService = maps;
        gameplayNoiseService = gameplayNoise;
    }

    public bool IsOpen => !disposed &&
                          sessionScope != null &&
                          !sessionScope.IsDisposed;

    internal IServiceResolver Services => IsOpen ? sessionRegistry : null;

    internal bool TryGetScope(out ServiceScope scope)
    {
        scope = IsOpen ? sessionScope : null;
        return scope != null;
    }

    internal bool TryOpen(out Exception failure)
    {
        failure = null;

        if (disposed)
        {
            failure = new ObjectDisposedException(nameof(SessionScopeController));
            return false;
        }

        if (IsOpen)
        {
            failure = new InvalidOperationException("Session service scope is already open.");
            return false;
        }

        ServiceScope candidateScope = null;
        SessionServiceRegistry candidateRegistry = null;
        PlayerScopeRegistry candidatePlayerRegistry = null;
        ServiceRegistrationTransaction transaction = null;

        try
        {
            candidateScope = globalScope.CreateChild(
                "Session",
                SessionContractPolicy.Instance);
            candidateRegistry = new SessionServiceRegistry(candidateScope);
            candidatePlayerRegistry = new PlayerScopeRegistry(candidateScope);
            transaction = candidateScope.BeginRegistrationTransaction();
            candidateScope.Register<ISessionServiceRegistry>(
                candidateRegistry,
                ServiceRegistrationOwnership.ScopeOwned);
            candidateScope.Register<IPlayerScopeRegistry>(
                candidatePlayerRegistry,
                ServiceRegistrationOwnership.ScopeOwned);
            candidateScope.Register<IGameMapSessionService>(gameMapService);
            candidateScope.Register<IGameplayNoiseService>(gameplayNoiseService);
            transaction.Commit();

            sessionScope = candidateScope;
            sessionRegistry = candidateRegistry;
            playerScopeRegistry = candidatePlayerRegistry;
            return true;
        }
        catch (Exception exception)
        {
            List<Exception> exceptions = new List<Exception> { exception };

            if (transaction != null)
                TryCleanup(transaction.Rollback, exceptions);

            if (candidateScope != null)
                TryCleanup(candidateScope.Dispose, exceptions);

            failure = exceptions.Count == 1
                ? exceptions[0]
                : new AggregateException("Failed to open Session service scope.", exceptions);

            return false;
        }
    }

    internal bool Close()
    {
        ServiceScope scope = sessionScope;
        PlayerScopeRegistry players = playerScopeRegistry;
        sessionScope = null;
        sessionRegistry = null;
        playerScopeRegistry = null;

        if (scope == null)
            return false;

        List<Exception> failures = new();

        if (players != null)
            TryCleanup(() => players.CloseAll(), failures);

        TryCleanup(scope.Dispose, failures);

        if (failures.Count > 0)
        {
            throw new AggregateException(
                "Failed to close Session service scope.",
                failures);
        }

        return true;
    }

    internal bool TryGetRegistry(out ISessionServiceRegistry registry)
    {
        registry = IsOpen ? sessionRegistry : null;
        return registry != null;
    }

    internal bool TryRegisterServices(
        Action<IServiceRegistrar> registerServices,
        out SessionServiceRegistration registrations,
        out Exception failure)
    {
        registrations = null;

        if (!IsOpen || sessionRegistry == null)
        {
            failure = new InvalidOperationException(
                "Cannot register services without an open Session scope.");

            return false;
        }

        return sessionRegistry.TryRegister(
            registerServices,
            out registrations,
            out failure);
    }

    internal bool TryOpenPlayerScope(
        ulong networkObjectId,
        ulong ownerClientId,
        bool isLocalPlayer,
        Action<IServiceRegistrar> registerReplicatedServices,
        Action<IServiceRegistrar> registerLocalServices,
        out PlayerScopeRegistration registration,
        out Exception failure)
    {
        registration = null;

        if (!IsOpen || playerScopeRegistry == null)
        {
            failure = new InvalidOperationException(
                "Cannot create a Player scope without an open Session scope.");

            return false;
        }

        return playerScopeRegistry.TryOpen(
            networkObjectId,
            ownerClientId,
            isLocalPlayer,
            registerReplicatedServices,
            registerLocalServices,
            out registration,
            out failure);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        Close();
    }

    private static void TryCleanup(Action cleanup, ICollection<Exception> exceptions)
    {
        if (cleanup == null)
            return;

        try
        {
            cleanup.Invoke();
        }
        catch (AggregateException aggregate)
        {
            for (int i = 0; i < aggregate.InnerExceptions.Count; i++)
                exceptions.Add(aggregate.InnerExceptions[i]);
        }
        catch (Exception exception)
        {
            exceptions.Add(exception);
        }
    }
}
