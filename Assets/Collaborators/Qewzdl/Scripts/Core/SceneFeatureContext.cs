using System;

public sealed class SceneFeatureContext
{
    internal SceneFeatureContext(
        int sceneHandle,
        ProjectSceneKind sceneKind,
        IServiceResolver services,
        ISceneServiceRegistrar registrar)
    {
        if (sceneHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(sceneHandle));

        SceneHandle = sceneHandle;
        SceneKind = sceneKind;
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
    }

    public int SceneHandle { get; }
    public ProjectSceneKind SceneKind { get; }
    public IServiceResolver Services { get; }
    public ISceneServiceRegistrar Registrar { get; }
}
