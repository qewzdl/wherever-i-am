using System;
using System.Collections.Generic;

internal sealed class SessionScopeController : IDisposable
{
    private readonly ServiceScope globalScope;
    private readonly IGameMapSessionService gameMapService;
    private readonly IGameplayNoiseService gameplayNoiseService;

    private ServiceScope sessionScope;
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

    public IServiceResolver Services => IsOpen ? sessionScope : null;

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
        ServiceRegistrationTransaction transaction = null;

        try
        {
            candidateScope = globalScope.CreateChild("Session");
            transaction = candidateScope.BeginRegistrationTransaction();
            candidateScope.Register<IGameMapSessionService>(gameMapService);
            candidateScope.Register<IGameplayNoiseService>(gameplayNoiseService);
            transaction.Commit();

            sessionScope = candidateScope;
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
        sessionScope = null;

        if (scope == null)
            return false;

        scope.Dispose();
        return true;
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
