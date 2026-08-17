using UnityEngine;

public class MusicCuePlayer : MonoBehaviour, IMusicServiceConsumer
{
    [Header("Cue")]
    [SerializeField] private MusicCue cue;

    [Header("Playback")]
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private bool stopOnDisable = false;
    [SerializeField] private bool restartIfSameCue = false;

    private IMusicService musicService;

    // Asked for on first use: this is a leaf, and whoever owns it may never
    // have thought to hand it anything.
    private IMusicService ResolvedMusicService => musicService ??= AudioServices.Music();

    public void Construct(IMusicService service)
    {
        musicService = service;
    }

    public void ReleaseMusicService()
    {
        musicService = null;
    }

    private void Start()
    {
        if (playOnStart)
        {
            Play();
        }
    }

    private void OnDisable()
    {
        if (stopOnDisable)
        {
            Stop();
        }
    }

    public void Play()
    {
        if (cue == null || !cue.IsValid)
        {
            Debug.LogWarning("MusicCuePlayer: MusicCue is missing or empty.");
            return;
        }

        if (ResolvedMusicService == null)
        {
            Debug.LogWarning("MusicCuePlayer: Music service was not constructed.");
            return;
        }

        ResolvedMusicService.PlayCue(cue, restartIfSameCue);
    }

    public void Stop()
    {
        if (ResolvedMusicService == null)
        {
            return;
        }

        ResolvedMusicService.StopMusic();
    }
}
