using System;
using System.Collections.Generic;
using UnityEngine;

internal interface ISessionServiceRegistrar
{
    void Register<TContract>(TContract service)
        where TContract : class;
}

internal sealed class SessionServiceRegistry : ISessionServiceRegistry, IDisposable
{
    private sealed class Registrar : ISessionServiceRegistrar
    {
        private readonly ServiceScope scope;
        private readonly List<ServiceRegistration> registrations;

        internal Registrar(
            ServiceScope serviceScope,
            List<ServiceRegistration> registrationHandles)
        {
            scope = serviceScope;
            registrations = registrationHandles;
        }

        public void Register<TContract>(TContract service)
            where TContract : class
        {
            ServiceRegistration registration = scope.Register<TContract>(
                service,
                ServiceRegistrationOwnership.UnityOwned);

            registrations.Add(registration);
        }
    }

    private readonly ServiceScope scope;
    private bool disposed;

    internal SessionServiceRegistry(ServiceScope serviceScope)
    {
        scope = serviceScope ?? throw new ArgumentNullException(nameof(serviceScope));
    }

    public bool IsDisposed => disposed || scope.IsDisposed;

    public event Action ServicesChanged;

    public T Resolve<T>() where T : class
    {
        EnsureActive();
        return scope.Resolve<T>();
    }

    public bool TryResolve<T>(out T service) where T : class
    {
        EnsureActive();
        return scope.TryResolve(out service);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        ServicesChanged = null;
    }

    internal bool TryRegister(
        Action<ISessionServiceRegistrar> registerServices,
        out SessionServiceRegistration registrations,
        out Exception failure)
    {
        registrations = null;
        failure = null;

        if (registerServices == null)
        {
            failure = new ArgumentNullException(nameof(registerServices));
            return false;
        }

        if (IsDisposed)
        {
            failure = new ObjectDisposedException(nameof(SessionServiceRegistry));
            return false;
        }

        ServiceRegistrationTransaction transaction = null;
        List<ServiceRegistration> registrationHandles = new();

        try
        {
            transaction = scope.BeginRegistrationTransaction();
            registerServices.Invoke(new Registrar(scope, registrationHandles));

            if (registrationHandles.Count == 0)
            {
                throw new InvalidOperationException(
                    "A Session service registration batch cannot be empty.");
            }

            transaction.Commit();
            registrations = new SessionServiceRegistration(this, registrationHandles);
            PublishServicesChanged();
            return true;
        }
        catch (Exception exception)
        {
            List<Exception> failures = new() { exception };

            if (transaction != null && !transaction.IsCompleted)
            {
                try
                {
                    transaction.Rollback();
                }
                catch (AggregateException aggregate)
                {
                    failures.AddRange(aggregate.InnerExceptions);
                }
                catch (Exception rollbackException)
                {
                    failures.Add(rollbackException);
                }
            }

            failure = failures.Count == 1
                ? failures[0]
                : new AggregateException(
                    "Failed to register Session services.",
                    failures);

            return false;
        }
    }

    internal void PublishServicesChanged()
    {
        if (IsDisposed)
            return;

        RuntimeEventDispatcher.Invoke(
            ServicesChanged,
            $"{nameof(SessionServiceRegistry)}.{nameof(ServicesChanged)}");
    }

    private void EnsureActive()
    {
        if (!IsDisposed)
            return;

        throw new ObjectDisposedException(nameof(SessionServiceRegistry));
    }
}

internal sealed class SessionServiceRegistration : IDisposable
{
    private SessionServiceRegistry registry;
    private List<ServiceRegistration> registrations;

    internal SessionServiceRegistration(
        SessionServiceRegistry owner,
        List<ServiceRegistration> registrationHandles)
    {
        registry = owner ?? throw new ArgumentNullException(nameof(owner));
        registrations = registrationHandles ??
                        throw new ArgumentNullException(nameof(registrationHandles));
    }

    public void Dispose()
    {
        List<ServiceRegistration> handles = registrations;

        if (handles == null)
            return;

        SessionServiceRegistry owner = registry;
        registrations = null;
        registry = null;
        List<Exception> failures = null;

        for (int i = handles.Count - 1; i >= 0; i--)
        {
            try
            {
                handles[i]?.Dispose();
            }
            catch (Exception exception)
            {
                failures ??= new List<Exception>();
                failures.Add(exception);
            }
        }

        owner?.PublishServicesChanged();

        if (failures != null)
        {
            throw new AggregateException(
                "Failed to unregister Session services.",
                failures);
        }
    }
}
