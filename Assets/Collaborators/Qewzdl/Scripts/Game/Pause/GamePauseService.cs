using System;
using UnityEngine;

public sealed class GamePauseService : MonoBehaviour, IPauseService
{
    [SerializeField] private GameStateMachine stateMachine;

    public bool IsPaused { get; private set; }

    public event Action<bool> PauseStateChanged;

    private bool stateMachineSubscribed;

    public void Construct(GameStateMachine stateMachine)
    {
        if (this.stateMachine == stateMachine)
        {
            SubscribeToStateMachine();
            return;
        }

        UnsubscribeFromStateMachine();
        this.stateMachine = stateMachine;
        SubscribeToStateMachine();
    }

    public void Dispose()
    {
        Resume();
        UnsubscribeFromStateMachine();
        stateMachine = null;
    }

    private void OnEnable()
    {
        SubscribeToStateMachine();
    }

    private void OnDisable()
    {
        UnsubscribeFromStateMachine();
    }

    public void Pause()
    {
        if (IsPaused)
            return;

        if (!CanPause())
            return;

        IsPaused = true;
        PauseStateChanged?.Invoke(IsPaused);
    }

    public void Resume()
    {
        if (!IsPaused)
            return;

        IsPaused = false;
        PauseStateChanged?.Invoke(IsPaused);
    }

    public void TogglePause()
    {
        if (IsPaused)
        {
            Resume();
            return;
        }

        Pause();
    }

    private bool CanPause()
    {
        if (stateMachine == null)
            return true;

        return stateMachine.CurrentState == GameState.InGame;
    }

    private void SubscribeToStateMachine()
    {
        if (stateMachineSubscribed || stateMachine == null)
            return;

        stateMachine.StateChanged += HandleGameStateChanged;
        stateMachineSubscribed = true;
    }

    private void UnsubscribeFromStateMachine()
    {
        if (!stateMachineSubscribed || stateMachine == null)
            return;

        stateMachine.StateChanged -= HandleGameStateChanged;
        stateMachineSubscribed = false;
    }

    private void HandleGameStateChanged(GameState previousState, GameState newState)
    {
        if (newState != GameState.InGame)
            Resume();
    }
}
