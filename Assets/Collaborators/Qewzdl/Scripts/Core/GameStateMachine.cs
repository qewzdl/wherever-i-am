using System;
using UnityEngine;

public class GameStateMachine : MonoBehaviour, IGameStateService
{
    public GameState CurrentState { get; private set; } = GameState.Bootstrapping;

    public event Action<GameState, GameState> StateChanged;

    public void ChangeState(GameState newState)
    {
        if (CurrentState == newState) return;

        GameState previousState = CurrentState;
        CurrentState = newState;

        StateChanged?.Invoke(previousState, newState);

        RuntimeLog.Info($"Game state changed: {previousState} -> {newState}");
    }
}
