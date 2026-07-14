public interface IMusicService
{
    MusicTrack CurrentTrack { get; }
    MusicCue CurrentCue { get; }
    bool IsPlaying { get; }

    void PlayCue(MusicCue cue, bool restartIfSameCue = false);
    void PlayTrack(MusicTrack track, bool restartIfSameTrack = false);
    void StopMusic(float fadeOutTime = 1f);
    void StopCue(MusicCue cue, float fadeOutTime = 1f);
    void Pause();
    void Resume();
    void SetMasterVolume(float volume);
}
