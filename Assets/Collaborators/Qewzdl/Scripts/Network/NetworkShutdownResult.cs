using System;

/// <summary>
/// Describes the terminal state reached by coordinated network shutdown.
/// A completed task does not imply success; callers must inspect
/// <see cref="Succeeded"/>.
/// </summary>
public readonly struct NetworkShutdownResult
{
    private NetworkShutdownResult(
        bool succeeded,
        bool networkStopped,
        bool sessionScopeClosed,
        bool mainMenuReady,
        string message,
        Exception exception)
    {
        Succeeded = succeeded;
        NetworkStopped = networkStopped;
        SessionScopeClosed = sessionScopeClosed;
        MainMenuReady = mainMenuReady;
        Message = message ?? string.Empty;
        Exception = exception;
    }

    public bool Succeeded { get; }
    public bool NetworkStopped { get; }
    public bool SessionScopeClosed { get; }
    public bool MainMenuReady { get; }
    public string Message { get; }
    public Exception Exception { get; }

    internal static NetworkShutdownResult Success()
    {
        return new NetworkShutdownResult(
            true,
            true,
            true,
            true,
            string.Empty,
            null);
    }

    internal static NetworkShutdownResult Failure(
        bool networkStopped,
        bool sessionScopeClosed,
        bool mainMenuReady,
        string message,
        Exception exception = null)
    {
        return new NetworkShutdownResult(
            false,
            networkStopped,
            sessionScopeClosed,
            mainMenuReady,
            message,
            exception);
    }
}
