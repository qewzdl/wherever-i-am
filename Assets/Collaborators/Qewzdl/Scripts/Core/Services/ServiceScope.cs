using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

internal sealed class ServiceScope : IServiceResolver, IDisposable
{
    private enum ScopeState
    {
        Active = 0,
        Disposing = 1,
        Disposed = 2
    }

    private sealed class ServiceEntry
    {
        public long Id;
        public Type ContractType;
        public object Service;
        public ServiceRegistrationOwnership Ownership;
        public OwnedServiceRecord OwnedService;
        public ServiceRegistration Registration;
        public bool IsActive;
    }

    private sealed class OwnedServiceRecord
    {
        public ServiceScope Owner;
        public object Service;
        public Action Cleanup;
        public Delegate ExplicitCleanup;
        public bool UsesDisposableCleanup;
        public int RegistrationCount;
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();

        public new bool Equals(object left, object right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(object value)
        {
            return RuntimeHelpers.GetHashCode(value);
        }
    }

    private readonly Dictionary<Type, ServiceEntry> services = new();
    private readonly Dictionary<long, ServiceEntry> registrationsById = new();
    private readonly List<ServiceEntry> registrationOrder = new();
    private readonly List<ServiceScope> children = new();
    private readonly Dictionary<object, OwnedServiceRecord> ownedServices;
    private readonly IServiceRegistrationPolicy registrationPolicy;

    private ServiceScope parent;
    private ServiceRegistrationTransaction activeTransaction;
    private ScopeState state;
    private long nextRegistrationId;

    internal ServiceScope(
        string name = null,
        IServiceRegistrationPolicy policy = null)
        : this(
            null,
            name,
            new Dictionary<object, OwnedServiceRecord>(ReferenceComparer.Instance),
            policy)
    {
    }

    private ServiceScope(
        ServiceScope parentScope,
        string name,
        Dictionary<object, OwnedServiceRecord> ownershipRegistry,
        IServiceRegistrationPolicy policy)
    {
        parent = parentScope;
        ownedServices = ownershipRegistry;
        registrationPolicy = policy;
        Name = string.IsNullOrWhiteSpace(name) ? nameof(ServiceScope) : name;
    }

    public string Name { get; }
    public bool IsDisposed => state == ScopeState.Disposed;
    public int LocalServiceCount => services.Count;
    public int ChildScopeCount => children.Count;

    public ServiceScope CreateChild(
        string name,
        IServiceRegistrationPolicy policy)
    {
        EnsureActive();

        if (policy == null)
            throw new ArgumentNullException(nameof(policy));

        ServiceScope child = new ServiceScope(this, name, ownedServices, policy);
        children.Add(child);
        return child;
    }

    public ServiceRegistration Register<TContract>(
        TContract service,
        ServiceRegistrationOwnership ownership = ServiceRegistrationOwnership.UnityOwned,
        ServiceShadowingPolicy shadowing = ServiceShadowingPolicy.Disallow,
        Action<TContract> cleanup = null)
        where TContract : class
    {
        EnsureActive();

        Type contractType = typeof(TContract);
        ValidateContractType(contractType);
        registrationPolicy?.ValidateRegistration(contractType, Name);

        if (service == null)
            throw new ArgumentNullException(nameof(service));

        if (!contractType.IsInstanceOfType(service))
        {
            throw new ArgumentException(
                $"Service '{service.GetType().Name}' does not implement '{contractType.Name}'.",
                nameof(service));
        }

        ValidateEnumValue(ownership, nameof(ownership));
        ValidateEnumValue(shadowing, nameof(shadowing));

        if (services.ContainsKey(contractType))
        {
            throw new InvalidOperationException(
                $"Scope '{Name}' already contains service contract '{contractType.Name}'.");
        }

        if (shadowing == ServiceShadowingPolicy.Disallow &&
            parent != null &&
            parent.ContainsInHierarchy(contractType))
        {
            throw new InvalidOperationException(
                $"Scope '{Name}' cannot shadow parent contract '{contractType.Name}' unless shadowing is explicitly allowed.");
        }

        OwnedServiceRecord ownedService = ownership == ServiceRegistrationOwnership.ScopeOwned
            ? AcquireOwnership(service, cleanup)
            : ValidateUnityOwnership(cleanup);
        long registrationId = GetNextRegistrationId();

        ServiceEntry entry = new ServiceEntry
        {
            Id = registrationId,
            ContractType = contractType,
            Service = service,
            Ownership = ownership,
            OwnedService = ownedService,
            IsActive = true
        };

        ServiceRegistration registration = new ServiceRegistration(this, registrationId, contractType);
        entry.Registration = registration;

        try
        {
            services.Add(contractType, entry);
            registrationsById.Add(registrationId, entry);
            registrationOrder.Add(entry);
            activeTransaction?.Track(registrationId);
        }
        catch
        {
            services.Remove(contractType);
            registrationsById.Remove(registrationId);
            registrationOrder.Remove(entry);
            registration.Detach();

            if (ownedService != null)
                ReleaseOwnership(ownedService, false);

            throw;
        }

        return registration;
    }

    public ServiceRegistrationTransaction BeginRegistrationTransaction()
    {
        EnsureActive();

        if (activeTransaction != null)
        {
            throw new InvalidOperationException(
                $"Scope '{Name}' already has an active registration transaction.");
        }

        activeTransaction = new ServiceRegistrationTransaction(this);
        return activeTransaction;
    }

    public T Resolve<T>() where T : class
    {
        if (TryResolve(out T service))
            return service;

        throw new KeyNotFoundException(
            $"Service contract '{typeof(T).Name}' is not registered in scope '{Name}' or its parents.");
    }

    public bool TryResolve<T>(out T service) where T : class
    {
        EnsureActive();

        Type contractType = typeof(T);
        ValidateContractType(contractType);

        if (TryResolveInHierarchy(contractType, out object resolved))
        {
            service = (T)resolved;
            return true;
        }

        service = null;
        return false;
    }

    internal bool HasLocalRegistration<TContract>() where TContract : class
    {
        EnsureActive();

        Type contractType = typeof(TContract);
        ValidateContractType(contractType);
        return services.ContainsKey(contractType);
    }

    public void Dispose()
    {
        DisposeInternal(true);
    }

    internal bool IsRegistrationActive(long registrationId)
    {
        return state == ScopeState.Active &&
               registrationsById.TryGetValue(registrationId, out ServiceEntry entry) &&
               entry.IsActive;
    }

    internal void Unregister(long registrationId)
    {
        if (state != ScopeState.Active ||
            !registrationsById.TryGetValue(registrationId, out ServiceEntry entry))
        {
            return;
        }

        RemoveEntry(entry);
    }

    internal void CommitTransaction(ServiceRegistrationTransaction transaction)
    {
        EnsureActive();

        if (!ReferenceEquals(activeTransaction, transaction))
            throw new InvalidOperationException($"Registration transaction does not belong to scope '{Name}'.");

        activeTransaction = null;
    }

    internal void RollbackTransaction(
        ServiceRegistrationTransaction transaction,
        IReadOnlyList<long> registrationIds)
    {
        if (!ReferenceEquals(activeTransaction, transaction))
            return;

        activeTransaction = null;

        if (state != ScopeState.Active)
            return;

        List<Exception> exceptions = null;

        for (int i = registrationIds.Count - 1; i >= 0; i--)
        {
            if (!registrationsById.TryGetValue(registrationIds[i], out ServiceEntry entry))
                continue;

            TryRemoveEntry(entry, ref exceptions);
        }

        ThrowCleanupExceptions(exceptions, $"Failed to roll back scope '{Name}'.");
    }

    private void DisposeInternal(bool notifyParent)
    {
        if (state != ScopeState.Active)
            return;

        state = ScopeState.Disposing;
        activeTransaction?.Detach();
        activeTransaction = null;

        List<Exception> exceptions = null;

        for (int i = children.Count - 1; i >= 0; i--)
        {
            try
            {
                children[i].DisposeInternal(false);
            }
            catch (Exception exception)
            {
                AddException(exception, ref exceptions);
            }
        }

        children.Clear();

        for (int i = registrationOrder.Count - 1; i >= 0; i--)
        {
            ServiceEntry entry = registrationOrder[i];

            if (entry.IsActive)
                TryRemoveEntry(entry, ref exceptions);
        }

        services.Clear();
        registrationsById.Clear();
        registrationOrder.Clear();
        state = ScopeState.Disposed;

        ServiceScope previousParent = parent;
        parent = null;

        if (notifyParent)
            previousParent?.RemoveChild(this);

        ThrowCleanupExceptions(exceptions, $"Failed to dispose scope '{Name}'.");
    }

    private bool TryResolveInHierarchy(Type contractType, out object service)
    {
        EnsureActive();

        if (services.TryGetValue(contractType, out ServiceEntry entry))
        {
            service = entry.Service;
            return true;
        }

        if (parent != null)
            return parent.TryResolveInHierarchy(contractType, out service);

        service = null;
        return false;
    }

    private bool ContainsInHierarchy(Type contractType)
    {
        EnsureActive();

        if (services.ContainsKey(contractType))
            return true;

        return parent != null && parent.ContainsInHierarchy(contractType);
    }

    private OwnedServiceRecord AcquireOwnership<TContract>(
        TContract service,
        Action<TContract> cleanup)
        where TContract : class
    {
        if (ownedServices.TryGetValue(service, out OwnedServiceRecord existing))
        {
            if (existing.Owner != this)
            {
                throw new InvalidOperationException(
                    $"Service instance '{service.GetType().Name}' is already owned by scope '{existing.Owner.Name}'.");
            }

            if (!UsesSameCleanup(existing, cleanup))
            {
                throw new InvalidOperationException(
                    $"Scope-owned service '{service.GetType().Name}' was registered with another cleanup strategy.");
            }

            existing.RegistrationCount++;
            return existing;
        }

        Action cleanupAction;
        bool usesDisposableCleanup;

        if (cleanup != null)
        {
            cleanupAction = () => cleanup(service);
            usesDisposableCleanup = false;
        }
        else if (service is IDisposable disposable)
        {
            cleanupAction = disposable.Dispose;
            usesDisposableCleanup = true;
        }
        else
        {
            throw new ArgumentException(
                $"Scope-owned service '{service.GetType().Name}' must implement {nameof(IDisposable)} or provide cleanup.",
                nameof(service));
        }

        OwnedServiceRecord record = new OwnedServiceRecord
        {
            Owner = this,
            Service = service,
            Cleanup = cleanupAction,
            ExplicitCleanup = cleanup,
            UsesDisposableCleanup = usesDisposableCleanup,
            RegistrationCount = 1
        };

        ownedServices.Add(service, record);
        return record;
    }

    private void RemoveEntry(ServiceEntry entry)
    {
        if (!entry.IsActive)
            return;

        entry.IsActive = false;
        services.Remove(entry.ContractType);
        registrationsById.Remove(entry.Id);
        entry.Registration.Detach();

        if (entry.OwnedService != null)
            ReleaseOwnership(entry.OwnedService, true);
    }

    private void TryRemoveEntry(ServiceEntry entry, ref List<Exception> exceptions)
    {
        try
        {
            RemoveEntry(entry);
        }
        catch (Exception exception)
        {
            AddException(exception, ref exceptions);
        }
    }

    private void RemoveChild(ServiceScope child)
    {
        if (state == ScopeState.Active)
            children.Remove(child);
    }

    private static OwnedServiceRecord ValidateUnityOwnership<TContract>(Action<TContract> cleanup)
        where TContract : class
    {
        if (cleanup == null)
            return null;

        throw new ArgumentException(
            "Unity-owned services cannot have scope cleanup. Use ScopeOwned ownership instead.",
            nameof(cleanup));
    }

    private static bool UsesSameCleanup<TContract>(
        OwnedServiceRecord existing,
        Action<TContract> cleanup)
        where TContract : class
    {
        if (cleanup == null)
            return true;

        return !existing.UsesDisposableCleanup && existing.ExplicitCleanup.Equals(cleanup);
    }

    private void ReleaseOwnership(OwnedServiceRecord ownedService, bool invokeCleanup)
    {
        ownedService.RegistrationCount--;

        if (ownedService.RegistrationCount > 0)
            return;

        ownedServices.Remove(ownedService.Service);

        if (invokeCleanup)
            ownedService.Cleanup.Invoke();
    }

    private long GetNextRegistrationId()
    {
        nextRegistrationId++;

        if (nextRegistrationId == 0)
            nextRegistrationId++;

        return nextRegistrationId;
    }

    private void EnsureActive()
    {
        if (state == ScopeState.Active)
            return;

        throw new ObjectDisposedException(Name, $"Service scope '{Name}' is not active.");
    }

    private static void ValidateContractType(Type contractType)
    {
        if (contractType.IsInterface)
            return;

        throw new ArgumentException(
            $"Service contract '{contractType.Name}' must be an interface.",
            nameof(contractType));
    }

    private static void ValidateEnumValue<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (Enum.IsDefined(typeof(TEnum), value))
            return;

        throw new ArgumentOutOfRangeException(parameterName, value, "Unsupported enum value.");
    }

    private static void AddException(Exception exception, ref List<Exception> exceptions)
    {
        exceptions ??= new List<Exception>();

        if (exception is AggregateException aggregate)
        {
            exceptions.AddRange(aggregate.InnerExceptions);
            return;
        }

        exceptions.Add(exception);
    }

    private static void ThrowCleanupExceptions(List<Exception> exceptions, string message)
    {
        if (exceptions != null && exceptions.Count > 0)
            throw new AggregateException(message, exceptions);
    }
}
