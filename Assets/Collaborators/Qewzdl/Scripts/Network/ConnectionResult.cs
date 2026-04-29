public class ConnectionResult 
{
    public bool Success { get; }
    public string Message { get; }

    private ConnectionResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    public static ConnectionResult Ok(string message) 
    {
        return new ConnectionResult(true, message);
    }

    public static ConnectionResult Fail(string message)
    {
        return new ConnectionResult(false, message);
    }
}
