public interface IProjectSceneFlowServerActionHandler
{
    bool CanHandle(ProjectSceneServerAction action);
    void Handle(ProjectSceneServerAction action, ProjectSceneKind loadedScene);
}