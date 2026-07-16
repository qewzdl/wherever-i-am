using System;

internal sealed class SceneServiceRegistrar : IServiceRegistrar
{
    private enum RegistrarState
    {
        Created = 0,
        Registering = 1,
        Closed = 2
    }

    private readonly ServiceScope scope;
    private RegistrarState state;

    internal SceneServiceRegistrar(ServiceScope serviceScope)
    {
        scope = serviceScope ?? throw new ArgumentNullException(nameof(serviceScope));
    }

    public void Register<TContract>(TContract service)
        where TContract : class
    {
        if (state != RegistrarState.Registering)
        {
            throw new InvalidOperationException(
                "Scene services can only be registered during feature installation.");
        }

        scope.Register<TContract>(service, ServiceRegistrationOwnership.UnityOwned);
    }

    internal void BeginRegistration()
    {
        if (state != RegistrarState.Created)
        {
            throw new InvalidOperationException(
                "Scene service registration phase has already started or completed.");
        }

        if (scope.IsDisposed)
            throw new ObjectDisposedException(scope.Name);

        state = RegistrarState.Registering;
    }

    internal void CloseRegistration()
    {
        state = RegistrarState.Closed;
    }
}
