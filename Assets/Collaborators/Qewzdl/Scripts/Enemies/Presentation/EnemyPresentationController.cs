using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyNetworkState))]
public class EnemyPresentationController : NetworkBehaviour, IGameplaySoundServiceConsumer
{
    // Every sound of hers is heard alike on every machine: the same take, at
    // the same pitch, or not at all, and at the same moment. What is rolled
    // comes from SoundRoll, seeded with things every machine already knows -
    // her network id and the numbered occasion. When comes from server time:
    // a sound with a delay, and each repeat of a looping one, is scheduled
    // against the moment the server says its occasion began, rather than
    // against whenever this machine happened to hear about it.
    //
    // The occasions are told apart by slot, so a state entry and a heard
    // noise that share a number do not share a roll.
    private const uint EnterSlot = 0u;
    private const uint LoopingSlot = 100u;
    private const uint HeardNoiseSlot = 200u;
    private const uint AnimationSlot = 300u;

    // A scheduled sound that comes due later than this, because this machine
    // learned of its occasion late, is skipped rather than played out of time.
    private const double StaleAfterSeconds = 1d;

    private sealed class LoopingSoundRuntime
    {
        public EnemyLoopingPresentationSound Sound;
        public uint Slot;
        public uint Occurrence;
        public double NextPlayAt;
    }

    private sealed class ScheduledSound
    {
        public SoundEffect Sound;
        public bool AtEnemyPosition;
        public double PlayAt;
        public SoundRoll Roll;
    }

    private static readonly List<EnemyPresentationController> activeControllers = new();

    public static event Action<EnemyPresentationController> Registered;
    public static event Action<EnemyPresentationController> Unregistered;

    public static IReadOnlyList<EnemyPresentationController> ActiveControllers => activeControllers;

    [Header("References")]
    [SerializeField] private EnemyNetworkState networkState;
    [SerializeField] private Animator animator;
    [SerializeField] private Transform soundOrigin;

    [Header("Profile")]
    [SerializeField] private EnemyPresentationProfile profile;

    private readonly List<ScheduledSound> scheduledSounds = new();
    private readonly List<LoopingSoundRuntime> activeLoopingSounds = new();
    private readonly Dictionary<string, uint> animationEventCounts = new();

    private EnemyState currentPresentedState = EnemyState.Idle;
    private EnemyAttackPhase currentPresentedAttackPhase = EnemyAttackPhase.Idle;
    private EnemyStatePresentation currentPresentation;
    private uint currentEntryNumber;
    private double currentEnteredAt;

    private readonly HashSet<EnemyState> warnedMissingPresentations = new();

    private ScheduledSound heardNoiseSound;
    private double nextHeardNoiseSoundAt = double.NegativeInfinity;

    private bool isRegistered;
    private bool subscribedToNetworkState;
    private IGameplaySoundService gameplaySoundService;

    // Asked for on first use: this is a leaf, and whoever owns it may never
    // have thought to hand it anything.
    private IGameplaySoundService ResolvedGameplaySoundService => gameplaySoundService ??= AudioServices.Gameplay();

    public EnemyAttackPhase CurrentAttackPhase => currentPresentedAttackPhase;

    private void Awake()
    {
        CacheComponents();
    }

    public override void OnNetworkSpawn()
    {
        CacheComponents();

        if (NetworkObjectServiceContext.TryResolveSessionService(
                NetworkManager,
                out IAudioService audioService))
        {
            Construct(audioService.Gameplay);
        }

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
        ReleaseGameplaySoundService();
    }

    public void Construct(IGameplaySoundService service)
    {
        gameplaySoundService = service;
    }

    public void ReleaseGameplaySoundService()
    {
        gameplaySoundService = null;
    }

    private void OnDisable()
    {
        CleanupClientPresentation();
    }

    private void Update()
    {
        if (!IsClient)
        {
            return;
        }

        double now = Now;

        for (int i = scheduledSounds.Count - 1; i >= 0; i--)
        {
            if (TryPlayWhenDue(scheduledSounds[i], now))
            {
                scheduledSounds.RemoveAt(i);
            }
        }

        if (heardNoiseSound != null && TryPlayWhenDue(heardNoiseSound, now))
        {
            heardNoiseSound = null;
        }

        for (int i = 0; i < activeLoopingSounds.Count; i++)
        {
            AdvanceLoopingSound(activeLoopingSounds[i], now);
        }
    }

    // The server's clock as this machine knows it, which is what every
    // schedule here is written in.
    private double Now =>
        NetworkManager != null && NetworkManager.IsListening
            ? NetworkManager.ServerTime.Time
            : Time.timeAsDouble;

    private bool TryPlayWhenDue(ScheduledSound scheduled, double now)
    {
        if (now < scheduled.PlayAt)
        {
            return false;
        }

        if (now - scheduled.PlayAt <= StaleAfterSeconds)
        {
            PlaySound(scheduled.Sound, scheduled.AtEnemyPosition, scheduled.Roll);
        }

        return true;
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

        // The n-th time this event fires in this entry into the state: the
        // animation runs on every machine from the same state, so the n-th is
        // the same footfall everywhere.
        animationEventCounts.TryGetValue(eventId, out uint count);
        animationEventCounts[eventId] = count + 1u;

        PlaySound(
            animationSound.Sound,
            animationSound.PlayAtEnemyPosition,
            RollFor(currentEntryNumber, AnimationSlot + StableHash(eventId) % 1000u, count));
    }

    private SoundRoll RollFor(uint occasion, uint slot, uint occurrence = 0u)
    {
        return SoundRoll.For(NetworkObjectId, occasion, slot, occurrence);
    }

    // string.GetHashCode is not promised to agree between two machines.
    private static uint StableHash(string text)
    {
        uint hash = 2166136261u;

        for (int i = 0; i < text.Length; i++)
        {
            hash = (hash ^ text[i]) * 16777619u;
        }

        return hash;
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
        animationEventCounts.Clear();

        // The state she was spawned in was entered at no time anybody wrote
        // down, so it starts when this machine first sees it.
        EnemyStateEntry entry = networkState.CurrentStateEntry;
        currentEntryNumber = entry.Number;
        currentEnteredAt = entry.IsNumbered ? entry.EnteredAt : Now;

        ResetPreviousTrigger(previousPresentation);

        if (!profile.TryGetPresentation(nextState, out EnemyStatePresentation nextPresentation))
        {
            WarnAboutMissingPresentation(nextState);
            return;
        }

        currentPresentation = nextPresentation;

        ApplyAnimatorState(nextPresentation);
        PlayEnterSounds(nextPresentation);
        ApplyLoopingSounds(nextPresentation);
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

    // She heard something and it was worth making a sound about.
    //
    // The threshold is checked here rather than on the server because it is a
    // question about the sound, not about her: the server reports every noise
    // it acts on, and what counts as loud enough to be worth hearing her react
    // to belongs beside the clip somebody chose. Nothing about her behaviour
    // reads it.
    //
    // The cooldown is not decoration. One noise lives in the world for the
    // whole hearing memory and perception re-reads it several times a second,
    // so the same bang arrives here again and again while she walks towards
    // it.
    private void HandleHeardNoise(float score, GameplayNoiseSourceType source)
    {
        // Whether a kind is worth exclaiming at is the profile's question -
        // see ShouldReactAloudTo, which is also where the reasoning is.
        if (profile == null || !profile.ShouldReactAloudTo(source))
            return;

        EnemyHeardNoiseSnapshot report = networkState.LastHeardNoise;

        if (profile == null ||
            profile.HeardLoudNoiseSound == null ||
            score < profile.HeardLoudNoiseScore ||
            report.HeardAt < nextHeardNoiseSoundAt)
        {
            return;
        }

        // Spent whether or not the roll comes up, so chance means "how often
        // she reacts" rather than "keep rolling until she does". Perception
        // re-reads the same noise several times a second, and a chance that
        // only consumed the cooldown on success would come up eventually every
        // single time. Measured between the server's times for the reports, so
        // every machine lets the same reports through.
        nextHeardNoiseSoundAt = report.HeardAt + profile.HeardLoudNoiseCooldown;

        EnemyPresentationSound reaction = profile.HeardLoudNoiseSound;
        SoundRoll roll = RollFor(report.Id, HeardNoiseSlot);

        if (!reaction.ShouldPlay(roll))
        {
            return;
        }

        // Its own schedule, and deliberately not one of the state sounds.
        //
        // Delayed state sounds are cancelled by the next state change, which is
        // right for them: they belong to the state that scheduled them, and a
        // growl for a chase that ended before it played would be a lie. This
        // one belongs to a noise, and hearing a noise worth reacting to is the
        // thing that sends her to Investigate - so on the shared list it
        // cancelled itself, every time, and only when a delay was set. The
        // cooldown had already been spent scheduling it, and the noise faded
        // from her hearing memory long before the cooldown was up, so she
        // reacted to it exactly never.
        heardNoiseSound = new ScheduledSound
        {
            Sound = reaction.Sound,
            AtEnemyPosition = reaction.PlayAtEnemyPosition,
            PlayAt = report.HeardAt + reaction.Delay,
            Roll = roll
        };
    }

    private void StopHeardNoiseSound()
    {
        heardNoiseSound = null;
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
            EnemyPresentationSound enterSound = enterSounds[i];
            SoundRoll roll = RollFor(currentEntryNumber, EnterSlot + (uint)i);

            if (enterSound == null || !enterSound.ShouldPlay(roll))
            {
                continue;
            }

            scheduledSounds.Add(new ScheduledSound
            {
                Sound = enterSound.Sound,
                AtEnemyPosition = enterSound.PlayAtEnemyPosition,
                PlayAt = currentEnteredAt + enterSound.Delay,
                Roll = roll
            });
        }

        // Due at once unless delayed - played now rather than a frame late.
        Update();
    }

    private void StopDelayedSounds()
    {
        scheduledSounds.Clear();
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

            uint slot = LoopingSlot + (uint)i;

            // The first repeat waits a rolled delay like every other, unless
            // the state says it starts with one.
            activeLoopingSounds.Add(new LoopingSoundRuntime
            {
                Sound = loopingSound,
                Slot = slot,
                Occurrence = 0u,
                NextPlayAt = currentEnteredAt +
                             (loopingSound.PlayImmediatelyOnEnter
                                 ? 0d
                                 : loopingSound.GetDelay(RollFor(currentEntryNumber, slot, 0u)))
            });
        }

        Update();
    }

    // Every repeat that has come due, in order: played if it is recent, and
    // passed over if this machine only now caught up with it. Each repeat
    // rolls its own take and the delay to the next, so the sequence is the
    // same everywhere however late anybody joined it.
    private void AdvanceLoopingSound(LoopingSoundRuntime runtime, double now)
    {
        if (runtime?.Sound == null || !runtime.Sound.IsValid)
        {
            return;
        }

        while (now >= runtime.NextPlayAt)
        {
            SoundRoll roll = RollFor(currentEntryNumber, runtime.Slot, runtime.Occurrence);

            if (now - runtime.NextPlayAt <= StaleAfterSeconds && runtime.Sound.ShouldPlay(roll))
            {
                PlaySound(runtime.Sound.Sound, runtime.Sound.PlayAtEnemyPosition, roll);
            }

            runtime.Occurrence++;
            runtime.NextPlayAt += runtime.Sound.GetDelay(
                RollFor(currentEntryNumber, runtime.Slot, runtime.Occurrence));
        }
    }

    private void StopLoopingSounds()
    {
        activeLoopingSounds.Clear();
    }

    private void PlaySound(SoundEffect sound, bool playAtEnemyPosition, SoundRoll roll)
    {
        if (sound == null)
        {
            return;
        }

        if (ResolvedGameplaySoundService == null)
        {
            return;
        }

        if (playAtEnemyPosition)
        {
            Transform origin = soundOrigin != null ? soundOrigin : transform;
            ResolvedGameplaySoundService.PlayAtPosition(sound, origin.position, roll);
            return;
        }

        ResolvedGameplaySoundService.Play2D(sound, roll);
    }

    // A state with no entry used to pass in silence, leaving the animator in
    // whatever pose it already held and the previous state's looping sounds
    // still going. An enemy stalking with a walk animation and patrol audio
    // looks like a bug in the behaviour rather than a missing asset, so the
    // gap says so - once per state, not once per transition.
    private void WarnAboutMissingPresentation(EnemyState state)
    {
        if (!warnedMissingPresentations.Add(state))
        {
            return;
        }

        Debug.LogWarning(
            $"{nameof(EnemyPresentationController)} has no presentation for " +
            $"{state}, so the animator and looping sounds stay on whatever " +
            "the previous state left. Add an entry for it to the profile.",
            this
        );
    }

    private void SubscribeToNetworkState()
    {
        if (subscribedToNetworkState || networkState == null)
        {
            return;
        }

        networkState.StateChanged += HandleStateChanged;
        networkState.AttackPhaseChanged += HandleAttackPhaseChanged;
        networkState.HeardNoise += HandleHeardNoise;

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
        networkState.HeardNoise -= HandleHeardNoise;

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

        Unregistered?.Invoke(this);
    }

    private void CleanupClientPresentation()
    {
        UnsubscribeFromNetworkState();
        UnregisterClientPresentation();

        StopHeardNoiseSound();
        StopDelayedSounds();
        StopLoopingSounds();

        currentPresentation = null;
        currentPresentedAttackPhase = EnemyAttackPhase.Idle;
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
