using System;

public interface IGameStateService
{
    GameState CurrentState { get; }

    event Action<GameState, GameState> StateChanged;

    void ChangeState(GameState newState);
}
