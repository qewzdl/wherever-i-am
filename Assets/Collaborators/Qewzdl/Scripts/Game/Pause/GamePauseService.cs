using System;
using UnityEngine;

public sealed class GamePauseService : MonoBehaviour, IPauseService
{
    [SerializeField] private GameStateMachine stateMachine;

    public bool IsPaused { get; private set; }

    public event Action<bool> PauseStateChanged;

    public void Construct(GameStateMachine stateMachine)
    {
        this.stateMachine = stateMachine;
    }

    private void OnEnable()
    {
        if (stateMachine != null)
            stateMachine.StateChanged += HandleGameStateChanged;
    }

    private void OnDisable()
    {
        if (stateMachine != null)
            stateMachine.StateChanged -= HandleGameStateChanged;
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

    private void HandleGameStateChanged(GameState previousState, GameState newState)
    {
        if (newState != GameState.InGame)
            Resume();
    }
}