public sealed class ConnectionResult
{
    public bool Success { get; }

    public ConnectionErrorCode ErrorCode { get; }

    public string UserMessage { get; }

    public string DebugMessage { get; }

    public bool CanRetry { get; }

    public string Message => UserMessage;

    private ConnectionResult(
        bool success,
        ConnectionErrorCode errorCode,
        string userMessage,
        string debugMessage,
        bool canRetry)
    {
        Success = success;
        ErrorCode = errorCode;
        UserMessage = userMessage;
        DebugMessage = string.IsNullOrWhiteSpace(debugMessage)
            ? userMessage
            : debugMessage;
        CanRetry = canRetry;
    }

    public static ConnectionResult Ok(string message = "")
    {
        return new ConnectionResult(
            true,
            ConnectionErrorCode.None,
            message,
            message,
            false
        );
    }

    public static ConnectionResult Fail(string message)
    {
        return Fail(
            ConnectionErrorCode.Unknown,
            message,
            message,
            true
        );
    }

    public static ConnectionResult Fail(
        ConnectionErrorCode errorCode,
        string userMessage,
        string debugMessage = "",
        bool canRetry = true)
    {
        return new ConnectionResult(
            false,
            errorCode,
            userMessage,
            debugMessage,
            canRetry
        );
    }
}