public interface IProjectSceneRegistry
{
    string GetSceneName(ProjectSceneKind sceneKind);
    string GetScenePath(ProjectSceneKind sceneKind);
    ProjectSceneKind GetActiveSceneKind();
    ProjectSceneKind GetSceneKind(string sceneName);
    ProjectSceneKind GetSceneKind(string sceneName, string scenePath);
    bool IsScene(ProjectSceneKind sceneKind, string sceneName);
    ProjectSceneKind GetBootstrapSceneKind();
    ProjectSceneKind GetDefaultStartupScene();
    GameState GetStateForScene(ProjectSceneKind sceneKind);
    bool TryGetScene(ProjectSceneKind sceneKind, out ProjectSceneDefinition scene);
}
