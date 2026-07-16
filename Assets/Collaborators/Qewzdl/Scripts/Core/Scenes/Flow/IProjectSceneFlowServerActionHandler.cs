using System;
using System.Threading;

public interface IProjectSceneFlowServerActionHandler
{
    bool CanHandle(ProjectSceneServerAction action);
    ProjectSceneActionResult Validate(
        ProjectSceneServerAction action,
        ProjectSceneKind loadedScene);
    ProjectSceneActionResult Execute(
        ProjectSceneServerAction action,
        ProjectSceneKind loadedScene);
}

public sealed class ProjectSceneActionResult
{
    private Action rollback;
    private int finalized;

    private ProjectSceneActionResult(
        bool succeeded,
        string error,
        Exception exception,
        Action rollbackAction)
    {
        Succeeded = succeeded;
        Error = succeeded || !string.IsNullOrWhiteSpace(error)
            ? error ?? string.Empty
            : "Project scene server action failed.";
        Exception = exception;
        rollback = rollbackAction;
    }

    public bool Succeeded { get; }
    public string Error { get; }
    public Exception Exception { get; }

    public static ProjectSceneActionResult Success(Action rollback = null)
    {
        return new ProjectSceneActionResult(true, string.Empty, null, rollback);
    }

    public static ProjectSceneActionResult Failure(
        string error,
        Exception exception = null,
        Action rollback = null)
    {
        return new ProjectSceneActionResult(false, error, exception, rollback);
    }

    internal void Commit()
    {
        if (Interlocked.Exchange(ref finalized, 1) != 0)
            return;

        rollback = null;
    }

    internal void Rollback()
    {
        if (Interlocked.Exchange(ref finalized, 1) != 0)
            return;

        Action rollbackAction = rollback;
        rollback = null;
        rollbackAction?.Invoke();
    }
}
