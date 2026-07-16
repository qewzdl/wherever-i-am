using System;

public sealed class SceneFeatureContext
{
    private Func<SceneFeatureContext, bool> requestScopeUninstall;

    internal SceneFeatureContext(
        int sceneHandle,
        ProjectSceneKind sceneKind,
        IServiceResolver services,
        IServiceRegistrar registrar,
        Func<SceneFeatureContext, bool> scopeUninstallRequest = null)
    {
        if (sceneHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(sceneHandle));

        SceneHandle = sceneHandle;
        SceneKind = sceneKind;
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
        requestScopeUninstall = scopeUninstallRequest;
    }

    public int SceneHandle { get; }
    public ProjectSceneKind SceneKind { get; }
    public IServiceResolver Services { get; }
    internal IServiceRegistrar Registrar { get; }

    /// <summary>
    /// Registers a Unity-owned scene service for the lifetime of this scene scope.
    /// This method is available only while the owning feature is being installed.
    /// </summary>
    public void Register<TContract>(TContract service)
        where TContract : class
    {
        Registrar.Register<TContract>(service);
    }

    internal bool RequestScopeUninstall()
    {
        Func<SceneFeatureContext, bool> request = requestScopeUninstall;
        return request != null && request.Invoke(this);
    }

    internal void DetachScopeLifetime()
    {
        requestScopeUninstall = null;
    }
}
