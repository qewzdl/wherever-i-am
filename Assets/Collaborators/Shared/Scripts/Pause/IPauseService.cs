using System;

public interface IPauseService
{
    bool IsPaused { get; }

    event Action<bool> PauseStateChanged;

    void Pause();
    void Resume();
    void TogglePause();
}
