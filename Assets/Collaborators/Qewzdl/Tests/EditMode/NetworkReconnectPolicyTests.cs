using NUnit.Framework;

// The host holds a dropped player's seat for a grace period. This is the only
// thing that decides whether the client ever comes back for it, so it is worth
// pinning down: everything it says yes to becomes a retry loop the player
// cannot cancel, and everything it says no to becomes a trip to the main menu.
public sealed class NetworkReconnectPolicyTests
{
    private const float Grace = 20f;

    private static bool ShouldAttempt(
        NetworkSessionState state = NetworkSessionState.InGame,
        bool isServer = false,
        string serverReason = "",
        string joinAddress = "192.168.0.5",
        float grace = Grace)
    {
        return NetworkReconnectPolicy.ShouldAttempt(
            state,
            isServer,
            serverReason,
            joinAddress,
            grace);
    }

    [Test]
    public void SilenceInARunningSessionIsWorthComingBackFrom()
    {
        Assert.That(ShouldAttempt(NetworkSessionState.Lobby), Is.True);
        Assert.That(ShouldAttempt(NetworkSessionState.LoadingGame), Is.True);
        Assert.That(ShouldAttempt(NetworkSessionState.InGame), Is.True);
    }

    // The difference between a lost connection and a decision is whether the
    // host said anything. Coming back from a kick would undo the kick.
    [Test]
    public void AReasonFromTheHostIsAnAnswerRatherThanAnAccident()
    {
        Assert.That(
            ShouldAttempt(serverReason: "The host removed you from the lobby."),
            Is.False);
        Assert.That(
            ShouldAttempt(serverReason: "The host closed the lobby."),
            Is.False);
    }

    // A join that never landed is usually the wrong address, and retrying it
    // only delays telling the player so.
    [Test]
    public void ASessionThatNeverStartedIsNotReconnected()
    {
        Assert.That(ShouldAttempt(NetworkSessionState.StartingClient), Is.False);
        Assert.That(ShouldAttempt(NetworkSessionState.LoadingLobby), Is.False);
        Assert.That(ShouldAttempt(NetworkSessionState.Offline), Is.False);
        Assert.That(ShouldAttempt(NetworkSessionState.Failed), Is.False);
        Assert.That(ShouldAttempt(NetworkSessionState.Disconnecting), Is.False);
    }

    [Test]
    public void AHostHasNothingToReconnectTo()
    {
        Assert.That(ShouldAttempt(isServer: true), Is.False);
    }

    [Test]
    public void WithoutAnAddressOrAWindowThereIsNothingToTry()
    {
        Assert.That(ShouldAttempt(joinAddress: string.Empty), Is.False);
        Assert.That(ShouldAttempt(joinAddress: "   "), Is.False);
        Assert.That(ShouldAttempt(grace: 0f), Is.False);
    }
}
