using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyNetworkState))]
public class EnemyPresentationController : NetworkBehaviour
{
    private sealed class LoopingSoundRuntime
    {
        public EnemyLoopingPresentationSound Sound;
        public float NextPlayTime;

        public LoopingSoundRuntime(EnemyLoopingPresentationSound sound)
        {
            Sound = sound;
        }
    }

    private static readonly List<EnemyPresentationController> activeControllers = new();

    public static event Action<EnemyPresentationController> Registered;
    public static event Action<EnemyPresentationController> Unregistered;
    public static event Action<EnemyPresentationController, EnemyThreatLevel> ThreatLevelChanged;

    public static IReadOnlyList<EnemyPresentationController> ActiveControllers => activeControllers;

    [Header("References")]
    [SerializeField] private EnemyNetworkState networkState;
    [SerializeField] private Animator animator;
    [SerializeField] private Transform soundOrigin;

    [Header("Profile")]
    [SerializeField] private EnemyPresentationProfile profile;

    private readonly List<Coroutine> delayedSoundRoutines = new();
    private readonly List<LoopingSoundRuntime> activeLoopingSounds = new();

    private EnemyState currentPresentedState = EnemyState.Idle;
    private EnemyAttackPhase currentPresentedAttackPhase = EnemyAttackPhase.Idle;
    private EnemyStatePresentation currentPresentation;
    private EnemyThreatLevel currentThreatLevel = EnemyThreatLevel.None;

    private bool isRegistered;
    private bool subscribedToNetworkState;

    public EnemyThreatLevel CurrentThreatLevel => currentThreatLevel;
    public EnemyAttackPhase CurrentAttackPhase => currentPresentedAttackPhase;

    private void Awake()
    {
        CacheComponents();
    }

    public override void OnNetworkSpawn()
    {
        CacheComponents();

        if (!IsClient)
        {
            return;
        }

        if (!ValidateDependencies())
        {
            enabled = false;
            return;
        }

        SubscribeToNetworkState();
        RegisterClientPresentation();

        ApplyState(networkState.CurrentState, force: true);
        ApplyAttackPhase(networkState.CurrentAttackPhase, force: true);
    }

    public override void OnNetworkDespawn()
    {
        CleanupClientPresentation();
    }

    private void OnDisable()
    {
        CleanupClientPresentation();
    }

    private void Update()
    {
        if (!IsClient || activeLoopingSounds.Count == 0)
        {
            return;
        }

        for (int i = 0; i < activeLoopingSounds.Count; i++)
        {
            LoopingSoundRuntime runtime = activeLoopingSounds[i];

            if (runtime == null || runtime.Sound == null || !runtime.Sound.IsValid)
            {
                continue;
            }

            if (Time.time < runtime.NextPlayTime)
            {
                continue;
            }

            if (runtime.Sound.ShouldPlay())
            {
                PlaySound(runtime.Sound.Sound, runtime.Sound.PlayAtEnemyPosition);
            }

            ScheduleNextLoopingSound(runtime);
        }
    }

    public void PlayAnimationSound(string eventId)
    {
        if (!IsClient || profile == null || string.IsNullOrWhiteSpace(eventId))
        {
            return;
        }

        if (!profile.TryGetAnimationSound(
            currentPresentedState,
            eventId,
            out EnemyAnimationSound animationSound))
        {
            return;
        }

        PlaySound(animationSound.Sound, animationSound.PlayAtEnemyPosition);
    }

    private void HandleStateChanged(EnemyState previousState, EnemyState nextState)
    {
        ApplyState(nextState, force: false);
    }

    private void HandleAttackPhaseChanged(
        EnemyAttackPhaseSnapshot previousPhase,
        EnemyAttackPhaseSnapshot nextPhase
    )
    {
        ApplyAttackPhase(nextPhase, force: false);
    }

    private void ApplyState(EnemyState nextState, bool force)
    {
        if (!force && currentPresentedState == nextState)
        {
            return;
        }

        EnemyStatePresentation previousPresentation = currentPresentation;

        StopDelayedSounds();
        StopLoopingSounds();

        currentPresentedState = nextState;
        currentPresentation = null;

        ResetPreviousTrigger(previousPresentation);

        if (!profile.TryGetPresentation(nextState, out EnemyStatePresentation nextPresentation))
        {
            SetThreatLevel(EnemyThreatLevel.None);
            return;
        }

        currentPresentation = nextPresentation;

        ApplyAnimatorState(nextPresentation);
        PlayEnterSounds(nextPresentation);
        ApplyLoopingSounds(nextPresentation);
        SetThreatLevel(nextPresentation.ThreatLevel);
    }

    private void ApplyAttackPhase(EnemyAttackPhaseSnapshot snapshot, bool force)
    {
        ApplyAttackPhase(snapshot.Phase, force);
    }

    private void ApplyAttackPhase(EnemyAttackPhase nextPhase, bool force)
    {
        if (!force && currentPresentedAttackPhase == nextPhase)
        {
            return;
        }

        currentPresentedAttackPhase = nextPhase;
        ApplyAnimatorAttackPhase(nextPhase);
    }

    private void ApplyAnimatorState(EnemyStatePresentation presentation)
    {
        if (animator == null || presentation == null || profile == null)
        {
            return;
        }

        if (profile.UseStateIntegerParameter &&
            !string.IsNullOrWhiteSpace(profile.StateIntegerParameter))
        {
            animator.SetInteger(
                profile.StateIntegerParameter,
                presentation.AnimatorStateValue
            );
        }

        if (!string.IsNullOrWhiteSpace(presentation.EnterTrigger))
        {
            animator.SetTrigger(presentation.EnterTrigger);
        }
    }

    private void ApplyAnimatorAttackPhase(EnemyAttackPhase attackPhase)
    {
        if (animator == null || profile == null)
        {
            return;
        }

        if (!profile.UseAttackPhaseIntegerParameter ||
            string.IsNullOrWhiteSpace(profile.AttackPhaseIntegerParameter))
        {
            return;
        }

        animator.SetInteger(
            profile.AttackPhaseIntegerParameter,
            (int)attackPhase
        );
    }

    private void ResetPreviousTrigger(EnemyStatePresentation previousPresentation)
    {
        if (animator == null || previousPresentation == null)
        {
            return;
        }

        if (!previousPresentation.ResetTriggerOnExit ||
            string.IsNullOrWhiteSpace(previousPresentation.EnterTrigger))
        {
            return;
        }

        animator.ResetTrigger(previousPresentation.EnterTrigger);
    }

    private void PlayEnterSounds(EnemyStatePresentation presentation)
    {
        if (presentation == null || !presentation.HasEnterSounds)
        {
            return;
        }

        EnemyPresentationSound[] enterSounds = presentation.EnterSounds;

        for (int i = 0; i < enterSounds.Length; i++)
        {
            PlayPresentationSound(enterSounds[i]);
        }
    }

    private void PlayPresentationSound(EnemyPresentationSound presentationSound)
    {
        if (presentationSound == null || !presentationSound.ShouldPlay())
        {
            return;
        }

        if (presentationSound.HasDelay)
        {
            Coroutine routine = StartCoroutine(PlayPresentationSoundDelayed(presentationSound));
            delayedSoundRoutines.Add(routine);
            return;
        }

        PlaySound(presentationSound.Sound, presentationSound.PlayAtEnemyPosition);
    }

    private IEnumerator PlayPresentationSoundDelayed(EnemyPresentationSound presentationSound)
    {
        yield return new WaitForSeconds(presentationSound.Delay);

        if (presentationSound == null || !presentationSound.IsValid)
        {
            yield break;
        }

        PlaySound(presentationSound.Sound, presentationSound.PlayAtEnemyPosition);
    }

    private void StopDelayedSounds()
    {
        for (int i = 0; i < delayedSoundRoutines.Count; i++)
        {
            Coroutine routine = delayedSoundRoutines[i];

            if (routine != null)
            {
                StopCoroutine(routine);
            }
        }

        delayedSoundRoutines.Clear();
    }

    private void ApplyLoopingSounds(EnemyStatePresentation presentation)
    {
        StopLoopingSounds();

        if (presentation == null || !presentation.HasLoopingSounds)
        {
            return;
        }

        EnemyLoopingPresentationSound[] loopingSounds = presentation.LoopingSounds;

        for (int i = 0; i < loopingSounds.Length; i++)
        {
            EnemyLoopingPresentationSound loopingSound = loopingSounds[i];

            if (loopingSound == null || !loopingSound.IsValid)
            {
                continue;
            }

            LoopingSoundRuntime runtime = new LoopingSoundRuntime(loopingSound);
            activeLoopingSounds.Add(runtime);

            if (loopingSound.PlayImmediatelyOnEnter && loopingSound.ShouldPlay())
            {
                PlaySound(loopingSound.Sound, loopingSound.PlayAtEnemyPosition);
            }

            ScheduleNextLoopingSound(runtime);
        }
    }

    private void ScheduleNextLoopingSound(LoopingSoundRuntime runtime)
    {
        if (runtime == null || runtime.Sound == null)
        {
            return;
        }

        runtime.NextPlayTime = Time.time + runtime.Sound.GetNextDelay();
    }

    private void StopLoopingSounds()
    {
        activeLoopingSounds.Clear();
    }

    private void PlaySound(SoundEffect sound, bool playAtEnemyPosition)
    {
        if (sound == null)
        {
            return;
        }

        AudioManager audioManager = AudioManager.Instance;

        if (audioManager == null || audioManager.Gameplay == null)
        {
            return;
        }

        if (playAtEnemyPosition)
        {
            Transform origin = soundOrigin != null ? soundOrigin : transform;
            audioManager.Gameplay.PlayAtPosition(sound, origin.position);
            return;
        }

        audioManager.Gameplay.Play2D(sound);
    }

    private void SetThreatLevel(EnemyThreatLevel nextThreatLevel)
    {
        if (currentThreatLevel == nextThreatLevel)
        {
            return;
        }

        currentThreatLevel = nextThreatLevel;
        ThreatLevelChanged?.Invoke(this, currentThreatLevel);
    }

    private void SubscribeToNetworkState()
    {
        if (subscribedToNetworkState || networkState == null)
        {
            return;
        }

        networkState.StateChanged += HandleStateChanged;
        networkState.AttackPhaseChanged += HandleAttackPhaseChanged;

        subscribedToNetworkState = true;
    }

    private void UnsubscribeFromNetworkState()
    {
        if (!subscribedToNetworkState || networkState == null)
        {
            return;
        }

        networkState.StateChanged -= HandleStateChanged;
        networkState.AttackPhaseChanged -= HandleAttackPhaseChanged;

        subscribedToNetworkState = false;
    }

    private void RegisterClientPresentation()
    {
        if (isRegistered)
        {
            return;
        }

        isRegistered = true;

        if (!activeControllers.Contains(this))
        {
            activeControllers.Add(this);
        }

        Registered?.Invoke(this);
    }

    private void UnregisterClientPresentation()
    {
        if (!isRegistered)
        {
            return;
        }

        isRegistered = false;
        activeControllers.Remove(this);

        ThreatLevelChanged?.Invoke(this, EnemyThreatLevel.None);
        Unregistered?.Invoke(this);
    }

    private void CleanupClientPresentation()
    {
        UnsubscribeFromNetworkState();
        UnregisterClientPresentation();

        StopDelayedSounds();
        StopLoopingSounds();

        currentPresentation = null;
        currentPresentedAttackPhase = EnemyAttackPhase.Idle;
        currentThreatLevel = EnemyThreatLevel.None;
    }

    private void CacheComponents()
    {
        if (networkState == null)
        {
            networkState = GetComponent<EnemyNetworkState>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (soundOrigin == null)
        {
            soundOrigin = transform;
        }
    }

    private bool ValidateDependencies()
    {
        if (networkState == null)
        {
            Debug.LogError($"{nameof(EnemyPresentationController)} requires {nameof(EnemyNetworkState)}.", this);
            return false;
        }

        if (profile == null)
        {
            Debug.LogError($"{nameof(EnemyPresentationController)} requires {nameof(EnemyPresentationProfile)}.", this);
            return false;
        }

        if (animator == null)
        {
            Debug.LogWarning(
                $"{nameof(EnemyPresentationController)} has no Animator. Audio and threat presentation will still work.",
                this
            );
        }

        return true;
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        CacheComponents();
    }
#endif
}