/// <summary>
/// Required health marker for every dynamically registered Session contract.
/// It is polled on the Unity main thread while Lobby or Game is active.
/// </summary>
internal interface ISessionServiceReadiness
{
    bool IsSessionServiceReady { get; }
}
