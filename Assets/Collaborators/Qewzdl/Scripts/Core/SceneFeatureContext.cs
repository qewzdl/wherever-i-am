using System;

public sealed class SceneFeatureContext
{
    private Func<SceneFeatureContext, bool> requestScopeUninstall;

    internal SceneFeatureContext(
        int sceneHandle,
        ProjectSceneKind sceneKind,
        IServiceResolver services,
        ISceneServiceRegistrar registrar,
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
    public ISceneServiceRegistrar Registrar { get; }

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
