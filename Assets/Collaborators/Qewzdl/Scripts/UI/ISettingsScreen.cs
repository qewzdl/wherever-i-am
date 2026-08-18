// The settings screen, as anything that wants to show it sees it.
//
// There is one for the whole game rather than one per scene, so this is a
// global contract: whoever offers a settings button - the pause menu, the main
// menu, a lobby - asks the scope for this and calls Open. None of them has to
// own a screen, or know that the screen outlives the scene they live in.
public interface ISettingsScreen
{
    bool IsOpen { get; }

    void Open();

    // Closes whatever state it is in, and may ask before it does: a screen
    // holding changes the player has not applied says so rather than throwing
    // them away. Callers that must be rid of it - a scene ending, a session
    // shutting down - get that for free, because it closes itself then.
    void Close();
}
