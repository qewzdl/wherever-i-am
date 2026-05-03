using UnityEngine;

public class MusicCuePlayer : MonoBehaviour
{
    [Header("Cue")]
    [SerializeField] private MusicCue cue;

    [Header("Playback")]
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private bool stopOnDisable = false;
    [SerializeField] private bool restartIfSameCue = false;

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

        if (AudioManager.Instance == null || AudioManager.Instance.Music == null)
        {
            Debug.LogWarning("MusicCuePlayer: AudioManager or MusicManager is missing.");
            return;
        }

        AudioManager.Instance.Music.PlayCue(cue, restartIfSameCue);
    }

    public void Stop()
    {
        if (AudioManager.Instance == null || AudioManager.Instance.Music == null)
        {
            return;
        }

        AudioManager.Instance.Music.StopMusic();
    }
}