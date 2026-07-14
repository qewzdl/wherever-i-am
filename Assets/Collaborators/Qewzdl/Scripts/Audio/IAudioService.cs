public interface IAudioService
{
    IMusicService Music { get; }
    IUiSoundService UI { get; }
    IGameplaySoundService Gameplay { get; }
}
