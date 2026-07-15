public interface IUiSoundServiceConsumer
{
    void Construct(IUiSoundService service);
    void ReleaseUiSoundService();
}

public interface IMusicServiceConsumer
{
    void Construct(IMusicService service);
    void ReleaseMusicService();
}

public interface IGameplaySoundServiceConsumer
{
    void Construct(IGameplaySoundService service);
    void ReleaseGameplaySoundService();
}
