using System;
using UnityEngine;

public sealed class GamePauseService : MonoBehaviour, IPauseService
{
    private IGameStateService stateService;

    public bool IsPaused { get; private set; }

    public event Action<bool> PauseStateChanged;

    private bool stateServiceSubscribed;

    public void Construct(IGameStateService gameStateService)
    {
        if (ReferenceEquals(stateService, gameStateService))
        {
            SubscribeToStateMachine();
            return;
        }

        UnsubscribeFromStateMachine();
        stateService = gameStateService;
        SubscribeToStateMachine();
    }

    public void Dispose()
    {
        Resume();
        UnsubscribeFromStateMachine();
        stateService = null;
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
        if (stateService == null)
            return true;

        return stateService.CurrentState == GameState.InGame;
    }

    private void SubscribeToStateMachine()
    {
        if (stateServiceSubscribed || stateService == null)
            return;

        stateService.StateChanged += HandleGameStateChanged;
        stateServiceSubscribed = true;
    }

    private void UnsubscribeFromStateMachine()
    {
        if (!stateServiceSubscribed || stateService == null)
            return;

        stateService.StateChanged -= HandleGameStateChanged;
        stateServiceSubscribed = false;
    }

    private void HandleGameStateChanged(GameState previousState, GameState newState)
    {
        if (newState != GameState.InGame)
            Resume();
    }
}
