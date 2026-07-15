using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyThreatMusicDirector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MusicManager musicManager;

    [Header("Cues")]
    [SerializeField] private MusicCue suspiciousCue;
    [SerializeField] private MusicCue combatCue;
    [SerializeField] private MusicCue lostTargetCue;
    [SerializeField] private MusicCue calmCue;

    [Header("Playback")]
    [SerializeField, Min(0f)] private float calmFadeOutTime = 1f;

    private readonly Dictionary<EnemyThreatMusicSource, EnemyThreatMusicState> activeThreats = new();

    private EnemyThreatMusicState currentAppliedThreatState = EnemyThreatMusicState.Calm;
    private MusicCue currentCue;

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        CacheReferences();

        EnemyThreatMusicSource.ThreatStateChanged += HandleThreatStateChanged;
        EnemyThreatMusicSource.SourceRemoved += HandleSourceRemoved;

        EnemyThreatMusicSource.CopyActiveThreatsTo(activeThreats);
        ApplyHighestThreatState(force: true);
    }

    private void OnDisable()
    {
        EnemyThreatMusicSource.ThreatStateChanged -= HandleThreatStateChanged;
        EnemyThreatMusicSource.SourceRemoved -= HandleSourceRemoved;

        activeThreats.Clear();
        StopCurrentCue(allowFadeOut: gameObject.activeInHierarchy);

        currentAppliedThreatState = EnemyThreatMusicState.Calm;
        currentCue = null;
    }

    private void HandleThreatStateChanged(
        EnemyThreatMusicSource source,
        EnemyThreatMusicState threatState
    )
    {
        if (source == null)
        {
            return;
        }

        if (threatState == EnemyThreatMusicState.Calm ||
            threatState == EnemyThreatMusicState.Dead)
        {
            activeThreats.Remove(source);
        }
        else
        {
            activeThreats[source] = threatState;
        }

        ApplyHighestThreatState(force: false);
    }

    private void HandleSourceRemoved(EnemyThreatMusicSource source)
    {
        if (source != null)
        {
            activeThreats.Remove(source);
        }

        ApplyHighestThreatState(force: false);
    }

    private void ApplyHighestThreatState(bool force)
    {
        EnemyThreatMusicState highestThreatState = GetHighestThreatState();

        if (!force && currentAppliedThreatState == highestThreatState)
        {
            return;
        }

        currentAppliedThreatState = highestThreatState;

        if (!TryGetCue(highestThreatState, out MusicCue nextCue))
        {
            StopCurrentCue(allowFadeOut: true);
            return;
        }

        PlayCue(nextCue);
    }

    private EnemyThreatMusicState GetHighestThreatState()
    {
        EnemyThreatMusicState highestThreatState = EnemyThreatMusicState.Calm;
        int highestPriority = GetThreatPriority(highestThreatState);

        foreach (EnemyThreatMusicState threatState in activeThreats.Values)
        {
            int priority = GetThreatPriority(threatState);

            if (priority > highestPriority)
            {
                highestPriority = priority;
                highestThreatState = threatState;
            }
        }

        return highestThreatState;
    }

    private static int GetThreatPriority(EnemyThreatMusicState threatState)
    {
        switch (threatState)
        {
            case EnemyThreatMusicState.Calm:
            case EnemyThreatMusicState.Dead:
                return 0;

            case EnemyThreatMusicState.Suspicious:
                return 1;

            case EnemyThreatMusicState.LostTarget:
                return 2;

            case EnemyThreatMusicState.Combat:
                return 3;

            default:
                return 0;
        }
    }

    private bool TryGetCue(EnemyThreatMusicState threatState, out MusicCue cue)
    {
        switch (threatState)
        {
            case EnemyThreatMusicState.Calm:
            case EnemyThreatMusicState.Dead:
                cue = calmCue;
                return cue != null;

            case EnemyThreatMusicState.Suspicious:
                cue = suspiciousCue;
                return cue != null;

            case EnemyThreatMusicState.Combat:
                cue = combatCue;
                return cue != null;

            case EnemyThreatMusicState.LostTarget:
                cue = lostTargetCue;
                return cue != null;

            default:
                Debug.LogError(
                    $"{nameof(EnemyThreatMusicDirector)} received unsupported threat music state {threatState}.",
                    this
                );

                cue = null;
                return false;
        }
    }

    private void PlayCue(MusicCue cue)
    {
        if (cue == null || musicManager == null)
        {
            return;
        }

        bool restartIfSameCue = currentCue != cue;
        currentCue = cue;
        musicManager.PlayCue(cue, restartIfSameCue);
    }

    private void StopCurrentCue(bool allowFadeOut)
    {
        if (musicManager == null)
        {
            return;
        }

        musicManager.StopMusic(allowFadeOut ? calmFadeOutTime : 0f);
        currentCue = null;
    }

    private void CacheReferences()
    {
        if (musicManager == null)
        {
            musicManager = GetComponent<MusicManager>();
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheReferences();
    }

    private void OnValidate()
    {
        calmFadeOutTime = Mathf.Max(0f, calmFadeOutTime);
        CacheReferences();
    }
#endif
}
