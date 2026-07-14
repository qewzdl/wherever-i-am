using System;

public interface IProjectSceneLoadCompletionGate
{
    bool CanHandle(ProjectSceneKind sceneKind);
    bool Validate(ProjectSceneKind sceneKind, out string error);
    bool BeginWait(ProjectSceneKind sceneKind, Action<bool> completed);
    void CancelPending(ProjectOperationCancelReason reason);
}
