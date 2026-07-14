using System;

public interface IProjectSceneFlowService
{
    bool HasPendingOperation { get; }

    event Action<ProjectSceneKind> SceneLoadCompleted;
    event Action<ProjectSceneKind> SceneLoadFailed;

    bool LoadScene(ProjectSceneKind targetScene);
    void CancelPendingOperations(ProjectOperationCancelReason reason);
}
