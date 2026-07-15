using System;
using System.Collections.Generic;
using UnityEngine;

internal interface IPlayerServiceRegistrar
{
    void Register<TContract>(TContract service)
        where TContract : class;
}

internal sealed class PlayerScopeRegistry : IPlayerScopeRegistry, IDisposable
{
    private sealed class Registrar : IPlayerServiceRegistrar
    {
        private readonly ServiceScope scope;

        internal Registrar(ServiceScope serviceScope)
        {
            scope = serviceScope;
        }

        public void Register<TContract>(TContract service)
            where TContract : class
        {
            scope.Register<TContract>(
                service,
                ServiceRegistrationOwnership.UnityOwned);
        }
    }

    private readonly ServiceScope sessionScope;
    private readonly Dictionary<ulong, PlayerRuntimeScope> scopes = new();
    private readonly List<ulong> scopeOrder = new();

    private PlayerRuntimeScope localPlayerScope;
    private bool disposed;

    internal PlayerScopeRegistry(ServiceScope parentScope)
    {
        sessionScope = parentScope ?? throw new ArgumentNullException(nameof(parentScope));
    }

    public bool IsDisposed => disposed || sessionScope.IsDisposed;

    public int Count
    {
        get
        {
            EnsureActive();
            return scopes.Count;
        }
    }

    public event Action<IPlayerScope> PlayerScopeOpened;
    public event Action<IPlayerScope> PlayerScopeClosing;

    public bool TryGetPlayerScope(
        ulong networkObjectId,
        out IPlayerScope playerScope)
    {
        EnsureActive();

        if (scopes.TryGetValue(networkObjectId, out PlayerRuntimeScope scope))
        {
            playerScope = scope;
            return true;
        }

        playerScope = null;
        return false;
    }

    public bool TryGetLocalPlayerScope(out IPlayerScope playerScope)
    {
        EnsureActive();
        playerScope = localPlayerScope;
        return playerScope != null && !playerScope.IsDisposed;
    }

    internal bool TryOpen(
        ulong networkObjectId,
        ulong ownerClientId,
        bool isLocalPlayer,
        Action<IPlayerServiceRegistrar> registerReplicatedServices,
        Action<IPlayerServiceRegistrar> registerLocalServices,
        out PlayerScopeRegistration registration,
        out Exception failure)
    {
        registration = null;
        failure = null;

        if (disposed || sessionScope.IsDisposed)
        {
            failure = new ObjectDisposedException(nameof(PlayerScopeRegistry));
            return false;
        }

        if (registerReplicatedServices == null)
        {
            failure = new ArgumentNullException(nameof(registerReplicatedServices));
            return false;
        }

        if (scopes.ContainsKey(networkObjectId))
        {
            failure = new InvalidOperationException(
                $"Player scope '{networkObjectId}' is already open.");

            return false;
        }

        if (isLocalPlayer && localPlayerScope != null)
        {
            failure = new InvalidOperationException(
                $"Local Player scope '{localPlayerScope.NetworkObjectId}' is already open.");

            return false;
        }

        ServiceScope playerScope = null;
        ServiceScope localScope = null;
        ServiceRegistrationTransaction replicatedTransaction = null;
        ServiceRegistrationTransaction localTransaction = null;

        try
        {
            playerScope = sessionScope.CreateChild(
                $"Player[{networkObjectId}]",
                PlayerContractPolicy.Instance);
            replicatedTransaction = playerScope.BeginRegistrationTransaction();
            registerReplicatedServices.Invoke(new Registrar(playerScope));

            if (playerScope.LocalServiceCount == 0)
            {
                throw new InvalidOperationException(
                    $"Player scope '{networkObjectId}' requires at least one replicated service.");
            }

            replicatedTransaction.Commit();

            if (isLocalPlayer)
            {
                if (registerLocalServices == null)
                {
                    throw new ArgumentNullException(nameof(registerLocalServices));
                }

                localScope = playerScope.CreateChild(
                    "Local",
                    LocalPlayerContractPolicy.Instance);
                localTransaction = localScope.BeginRegistrationTransaction();
                registerLocalServices.Invoke(new Registrar(localScope));

                if (localScope.LocalServiceCount == 0)
                {
                    throw new InvalidOperationException(
                        $"Local Player scope '{networkObjectId}' requires at least one local service.");
                }

                localTransaction.Commit();
            }

            PlayerRuntimeScope runtimeScope = new(
                networkObjectId,
                ownerClientId,
                isLocalPlayer,
                playerScope,
                localScope);

            scopes.Add(networkObjectId, runtimeScope);
            scopeOrder.Add(networkObjectId);

            if (isLocalPlayer)
                localPlayerScope = runtimeScope;

            registration = new PlayerScopeRegistration(this, runtimeScope);
            Publish(PlayerScopeOpened, runtimeScope, nameof(PlayerScopeOpened));
            return true;
        }
        catch (Exception exception)
        {
            List<Exception> failures = new() { exception };
            TryRollback(localTransaction, failures);
            TryRollback(replicatedTransaction, failures);

            if (playerScope != null)
                TryCleanup(playerScope.Dispose, failures);

            failure = failures.Count == 1
                ? failures[0]
                : new AggregateException(
                    $"Failed to open Player scope '{networkObjectId}'.",
                    failures);

            return false;
        }
    }

    internal int CloseAll()
    {
        if (scopes.Count == 0)
            return 0;

        int closedCount = 0;
        List<Exception> failures = null;

        for (int i = scopeOrder.Count - 1; i >= 0; i--)
        {
            ulong networkObjectId = scopeOrder[i];

            if (!scopes.TryGetValue(networkObjectId, out PlayerRuntimeScope scope))
                continue;

            try
            {
                if (Close(networkObjectId, scope))
                    closedCount++;
            }
            catch (Exception exception)
            {
                failures ??= new List<Exception>();
                failures.Add(exception);
            }
        }

        if (failures != null)
        {
            throw new AggregateException(
                "Failed to close all Player scopes.",
                failures);
        }

        return closedCount;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        try
        {
            CloseAll();
        }
        finally
        {
            disposed = true;
            PlayerScopeOpened = null;
            PlayerScopeClosing = null;
        }
    }

    internal bool Close(
        ulong networkObjectId,
        PlayerRuntimeScope expectedScope)
    {
        if (!scopes.TryGetValue(networkObjectId, out PlayerRuntimeScope scope) ||
            !ReferenceEquals(scope, expectedScope))
        {
            return false;
        }

        scopes.Remove(networkObjectId);
        scopeOrder.Remove(networkObjectId);

        if (ReferenceEquals(localPlayerScope, scope))
            localPlayerScope = null;

        Publish(PlayerScopeClosing, scope, nameof(PlayerScopeClosing));
        scope.Dispose();
        return true;
    }

    private static void TryRollback(
        ServiceRegistrationTransaction transaction,
        ICollection<Exception> failures)
    {
        if (transaction == null || transaction.IsCompleted)
            return;

        TryCleanup(transaction.Rollback, failures);
    }

    private static void TryCleanup(
        Action cleanup,
        ICollection<Exception> failures)
    {
        try
        {
            cleanup.Invoke();
        }
        catch (AggregateException aggregate)
        {
            for (int i = 0; i < aggregate.InnerExceptions.Count; i++)
                failures.Add(aggregate.InnerExceptions[i]);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static void Publish(
        Action<IPlayerScope> handlers,
        IPlayerScope playerScope,
        string eventName)
    {
        if (handlers == null)
            return;

        Delegate[] subscribers = handlers.GetInvocationList();

        for (int i = 0; i < subscribers.Length; i++)
        {
            try
            {
                ((Action<IPlayerScope>)subscribers[i]).Invoke(playerScope);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Subscriber failed while handling {eventName}.");
                Debug.LogException(exception);
            }
        }
    }

    private void EnsureActive()
    {
        if (!disposed && !sessionScope.IsDisposed)
            return;

        throw new ObjectDisposedException(nameof(PlayerScopeRegistry));
    }
}

internal sealed class PlayerRuntimeScope : IPlayerScope, IDisposable
{
    private ServiceScope playerScope;
    private ServiceScope localScope;

    internal PlayerRuntimeScope(
        ulong networkObjectId,
        ulong ownerClientId,
        bool isLocalPlayer,
        ServiceScope replicatedServices,
        ServiceScope localServices)
    {
        NetworkObjectId = networkObjectId;
        OwnerClientId = ownerClientId;
        IsLocalPlayer = isLocalPlayer;
        playerScope = replicatedServices ??
                      throw new ArgumentNullException(nameof(replicatedServices));
        localScope = localServices;
    }

    public ulong NetworkObjectId { get; }
    public ulong OwnerClientId { get; }
    public bool IsLocalPlayer { get; }
    public bool IsDisposed => playerScope == null || playerScope.IsDisposed;
    public IServiceResolver Services => !IsDisposed ? playerScope : null;
    public IServiceResolver LocalServices => !IsDisposed &&
                                             localScope != null &&
                                             !localScope.IsDisposed
        ? localScope
        : null;

    public void Dispose()
    {
        ServiceScope scope = playerScope;
        playerScope = null;
        localScope = null;
        scope?.Dispose();
    }
}

internal sealed class PlayerScopeRegistration : IDisposable
{
    private PlayerScopeRegistry registry;
    private PlayerRuntimeScope playerScope;

    internal PlayerScopeRegistration(
        PlayerScopeRegistry owner,
        PlayerRuntimeScope scope)
    {
        registry = owner ?? throw new ArgumentNullException(nameof(owner));
        playerScope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    public void Dispose()
    {
        PlayerScopeRegistry owner = registry;
        PlayerRuntimeScope scope = playerScope;
        registry = null;
        playerScope = null;

        if (owner != null && scope != null)
            owner.Close(scope.NetworkObjectId, scope);
    }
}
