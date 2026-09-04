using System;

// A chat window, whatever it is made of.
//
// There are two of them now and they have nothing in common below this line.
// The lobby's is a UI Toolkit document that reads from the same theme as every
// other screen in the game; the phone's is the old uGUI window, and it stays
// that way on purpose - it is not a screen, it is a prop, a canvas hung on a
// three-dimensional phone that slides into frame and animates its own case.
// Moving that to UI Toolkit means a world-space panel and a rewrite of the
// thing that puts it there, which is a different job from this one.
//
// So the parts that neither of them owns - the session binder, the key that
// opens it, the unread counter - talk to this instead of to either.
public interface IChatWindowView
{
    event Action Opened;
    event Action Closed;

    bool IsOpen { get; }
    bool IsInputFocused { get; }
    bool CanOpen { get; }

    void Open();
    void Close();
    void Toggle();

    void Construct(
        IChatReadService readService,
        IChatCommandService commandService,
        IGameStateService stateMachine,
        ILocalPlayerInputService inputService);

    void SetLocalInputService(ILocalPlayerInputService inputService);
    void SetEventChannel(ChatEventChannel chatEvents);
    void SetInputSoundOverride(SoundEffect sound);
}
