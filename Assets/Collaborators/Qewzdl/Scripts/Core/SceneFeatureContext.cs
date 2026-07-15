using System;

public sealed class SceneFeatureContext
{
    internal SceneFeatureContext(
        int sceneHandle,
        ProjectSceneKind sceneKind,
        IServiceResolver services)
    {
        if (sceneHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(sceneHandle));

        SceneHandle = sceneHandle;
        SceneKind = sceneKind;
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public int SceneHandle { get; }
    public ProjectSceneKind SceneKind { get; }
    public IServiceResolver Services { get; }
}
