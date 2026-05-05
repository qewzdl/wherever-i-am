using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyThreatMusicDirector : MonoBehaviour
{
    [Header("No Threat")]
    [SerializeField] private MusicCue noThreatCue;
    [SerializeField] private bool restoreNoThreatCue = true;

    [Header("Threat Music")]
    [SerializeField] private EnemyThreatMusicEntry[] musicByThreatLevel;

    private readonly Dictionary<EnemyPresentationController, EnemyThreatLevel> threatLevels = new();

    private EnemyThreatLevel currentAppliedThreatLevel = EnemyThreatLevel.None;

    private void OnEnable()
    {
        EnemyPresentationController.Registered += HandleEnemyRegistered;
        EnemyPresentationController.Unregistered += HandleEnemyUnregistered;
        EnemyPresentationController.ThreatLevelChanged += HandleThreatLevelChanged;

        RegisterExistingEnemies();
        ApplyHighestThreatLevel(force: true);
    }

    private void OnDisable()
    {
        EnemyPresentationController.Registered -= HandleEnemyRegistered;
        EnemyPresentationController.Unregistered -= HandleEnemyUnregistered;
        EnemyPresentationController.ThreatLevelChanged -= HandleThreatLevelChanged;

        threatLevels.Clear();
        currentAppliedThreatLevel = EnemyThreatLevel.None;
    }

    private void RegisterExistingEnemies()
    {
        IReadOnlyList<EnemyPresentationController> activeControllers =
            EnemyPresentationController.ActiveControllers;

        for (int i = 0; i < activeControllers.Count; i++)
        {
            EnemyPresentationController controller = activeControllers[i];

            if (controller == null)
            {
                continue;
            }

            threatLevels[controller] = controller.CurrentThreatLevel;
        }
    }

    private void HandleEnemyRegistered(EnemyPresentationController controller)
    {
        if (controller == null)
        {
            return;
        }

        threatLevels[controller] = controller.CurrentThreatLevel;
        ApplyHighestThreatLevel(force: false);
    }

    private void HandleEnemyUnregistered(EnemyPresentationController controller)
    {
        if (controller == null)
        {
            return;
        }

        threatLevels.Remove(controller);
        ApplyHighestThreatLevel(force: false);
    }

    private void HandleThreatLevelChanged(
        EnemyPresentationController controller,
        EnemyThreatLevel threatLevel
    )
    {
        if (controller == null)
        {
            return;
        }

        if (threatLevel == EnemyThreatLevel.None)
        {
            threatLevels.Remove(controller);
        }
        else
        {
            threatLevels[controller] = threatLevel;
        }

        ApplyHighestThreatLevel(force: false);
    }

    private void ApplyHighestThreatLevel(bool force)
    {
        EnemyThreatLevel highestThreatLevel = GetHighestThreatLevel();

        if (!force && currentAppliedThreatLevel == highestThreatLevel)
        {
            return;
        }

        currentAppliedThreatLevel = highestThreatLevel;

        if (highestThreatLevel == EnemyThreatLevel.None)
        {
            ApplyNoThreatMusic();
            return;
        }

        if (!TryGetCue(highestThreatLevel, out MusicCue cue, out bool restartIfSameCue))
        {
            return;
        }

        PlayCue(cue, restartIfSameCue);
    }

    private void ApplyNoThreatMusic()
    {
        if (!restoreNoThreatCue || noThreatCue == null)
        {
            return;
        }

        PlayCue(noThreatCue, restartIfSameCue: false);
    }

    private void PlayCue(MusicCue cue, bool restartIfSameCue)
    {
        if (cue == null)
        {
            return;
        }

        AudioManager audioManager = AudioManager.Instance;

        if (audioManager == null || audioManager.Music == null)
        {
            return;
        }

        audioManager.Music.PlayCue(cue, restartIfSameCue);
    }

    private EnemyThreatLevel GetHighestThreatLevel()
    {
        EnemyThreatLevel highestThreatLevel = EnemyThreatLevel.None;

        foreach (EnemyThreatLevel threatLevel in threatLevels.Values)
        {
            if (threatLevel > highestThreatLevel)
            {
                highestThreatLevel = threatLevel;
            }
        }

        return highestThreatLevel;
    }

    private bool TryGetCue(
        EnemyThreatLevel threatLevel,
        out MusicCue cue,
        out bool restartIfSameCue
    )
    {
        cue = null;
        restartIfSameCue = false;

        if (musicByThreatLevel == null)
        {
            return false;
        }

        for (int i = 0; i < musicByThreatLevel.Length; i++)
        {
            EnemyThreatMusicEntry entry = musicByThreatLevel[i];

            if (entry == null || entry.threatLevel != threatLevel || entry.cue == null)
            {
                continue;
            }

            cue = entry.cue;
            restartIfSameCue = entry.restartIfSameCue;
            return true;
        }

        return false;
    }
}