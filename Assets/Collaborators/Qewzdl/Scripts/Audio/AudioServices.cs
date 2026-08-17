// Where a sound consumer gets its service when nobody hands one over. The
// consumers are many, anonymous and scattered - a button on every menu - so
// they ask rather than wait to be found by a sweep of the scene.
public static class AudioServices
{
    public static IUiSoundService Ui()
    {
        return G.TryResolve(out IAudioService audioService)
            ? audioService.UI
            : null;
    }

    public static IMusicService Music()
    {
        return G.TryResolve(out IAudioService audioService)
            ? audioService.Music
            : null;
    }

    public static IGameplaySoundService Gameplay()
    {
        return G.TryResolve(out IAudioService audioService)
            ? audioService.Gameplay
            : null;
    }
}
