using NUnit.Framework;

// The one line above the lobby's actions, and which of six true things it says.
//
// The order is the whole feature: at any moment two or three of these are
// equally true, and which one a player is shown decides whether they read the
// line as their own problem or as somebody else's. That is what these pin down.
public sealed class LobbyStartHintTests
{
    [Test]
    public void AMatchAlreadyStartingDrownsOutEverythingElse()
    {
        Assert.That(
            LobbyUI.ChooseStartHint(
                isLobbyPhaseOpen: false,
                missingPlayers: 3,
                notReadyCount: 4,
                isLocalPlayerReady: false,
                isLocalPlayerRoomOwner: true),
            Is.EqualTo(LobbyUI.StartHint.Starting));
    }

    // Readying up in a room that cannot start either way is work with nothing
    // at the end of it, so the empty chair is named first.
    [Test]
    public void AnEmptyChairIsNamedBeforeAnUnreadyPlayer()
    {
        Assert.That(
            LobbyUI.ChooseStartHint(
                isLobbyPhaseOpen: true,
                missingPlayers: 1,
                notReadyCount: 2,
                isLocalPlayerReady: false,
                isLocalPlayerRoomOwner: false),
            Is.EqualTo(LobbyUI.StartHint.NeedMorePlayers));
    }

    [Test]
    public void YourOwnReadinessComesBeforeAnybodyElses()
    {
        // Three people are holding the room up and one of them is you. The only
        // one of the three you can do anything about is you.
        Assert.That(
            LobbyUI.ChooseStartHint(
                isLobbyPhaseOpen: true,
                missingPlayers: 0,
                notReadyCount: 3,
                isLocalPlayerReady: false,
                isLocalPlayerRoomOwner: false),
            Is.EqualTo(LobbyUI.StartHint.ReadyUpYourself));

        // Including the host, who has a Start button and still cannot use it
        // until they have answered for themselves.
        Assert.That(
            LobbyUI.ChooseStartHint(
                isLobbyPhaseOpen: true,
                missingPlayers: 0,
                notReadyCount: 1,
                isLocalPlayerReady: false,
                isLocalPlayerRoomOwner: true),
            Is.EqualTo(LobbyUI.StartHint.ReadyUpYourself));
    }

    [Test]
    public void OnePersonIsNamedAndSeveralAreCounted()
    {
        Assert.That(
            LobbyUI.ChooseStartHint(
                isLobbyPhaseOpen: true,
                missingPlayers: 0,
                notReadyCount: 1,
                isLocalPlayerReady: true,
                isLocalPlayerRoomOwner: false),
            Is.EqualTo(LobbyUI.StartHint.WaitingForOne));

        Assert.That(
            LobbyUI.ChooseStartHint(
                isLobbyPhaseOpen: true,
                missingPlayers: 0,
                notReadyCount: 2,
                isLocalPlayerReady: true,
                isLocalPlayerRoomOwner: false),
            Is.EqualTo(LobbyUI.StartHint.WaitingForSeveral));
    }

    // Nothing left to wait for, and the two people reading it have opposite
    // things left to do: one presses Start, the other waits for them to.
    [Test]
    public void WithEverybodyReadyTheHostIsToldToStartAndTheRestAreToldToWait()
    {
        Assert.That(
            LobbyUI.ChooseStartHint(
                isLobbyPhaseOpen: true,
                missingPlayers: 0,
                notReadyCount: 0,
                isLocalPlayerReady: true,
                isLocalPlayerRoomOwner: true),
            Is.EqualTo(LobbyUI.StartHint.EveryoneReady));

        Assert.That(
            LobbyUI.ChooseStartHint(
                isLobbyPhaseOpen: true,
                missingPlayers: 0,
                notReadyCount: 0,
                isLocalPlayerReady: true,
                isLocalPlayerRoomOwner: false),
            Is.EqualTo(LobbyUI.StartHint.WaitingForHost));
    }

    // Readiness can be switched off for the room, and then it never holds a
    // start up - the caller passes nought and the line skips straight past the
    // waiting branches rather than naming somebody who is not the reason.
    [Test]
    public void ReadinessThatIsNotRequiredNeverHoldsTheRoomUp()
    {
        Assert.That(
            LobbyUI.ChooseStartHint(
                isLobbyPhaseOpen: true,
                missingPlayers: 0,
                notReadyCount: 0,
                isLocalPlayerReady: false,
                isLocalPlayerRoomOwner: true),
            Is.EqualTo(LobbyUI.StartHint.EveryoneReady));
    }
}
