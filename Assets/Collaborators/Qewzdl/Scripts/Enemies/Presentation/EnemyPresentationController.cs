using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyNetworkState))]
public class EnemyPresentationController : NetworkBehaviour
{
    private static readonly List<EnemyPresentationController> activeControllers = new();

    public static event Action<EnemyPresentationController> Registered;
    public static event Action<EnemyPresentationController> Unregistered;
    public static event Action<EnemyPresentationController, EnemyThreatLevel> ThreatLevelChanged;

    public static IReadOnlyList<EnemyPresentationController> ActiveControllers => activeControllers;

    [Header("References")]
    [SerializeField] private EnemyNetworkState networkState;
    [SerializeField] private Animator animator;

    [Header("Presentation")]
    [SerializeField] private EnemyPresentationProfile profile;
    [SerializeField] private Transform soundOrigin;

    private EnemyState currentPresentedState = EnemyState.Idle;
    private EnemyThreatLevel currentThreatLevel = EnemyThreatLevel.None;
    private bool isRegistered;

    public EnemyThreatLevel CurrentThreatLevel => currentThreatLevel;

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

        networkState.StateChanged += HandleStateChanged;

        RegisterClientPresentation();
        ApplyState(networkState.CurrentState, force: true);
    }

    public override void OnNetworkDespawn()
    {
        if (networkState != null)
        {
            networkState.StateChanged -= HandleStateChanged;
        }

        UnregisterClientPresentation();
    }

    private void HandleStateChanged(EnemyState previousState, EnemyState nextState)
    {
        ApplyState(nextState, force: false);
    }

    private void ApplyState(EnemyState nextState, bool force)
    {
        if (!force && currentPresentedState == nextState)
        {
            return;
        }

        EnemyState previousState = currentPresentedState;
        currentPresentedState = nextState;

        ResetPreviousTrigger(previousState);

        if (!profile.TryGetPresentation(nextState, out EnemyStatePresentation presentation))
        {
            SetThreatLevel(EnemyThreatLevel.None);
            return;
        }

        ApplyAnimatorState(presentation);
        PlayEnterSound(presentation);
        SetThreatLevel(presentation.ThreatLevel);
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

    private void ResetPreviousTrigger(EnemyState previousState)
    {
        if (animator == null || profile == null)
        {
            return;
        }

        if (!profile.TryGetPresentation(previousState, out EnemyStatePresentation previousPresentation))
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

    private void PlayEnterSound(EnemyStatePresentation presentation)
    {
        if (presentation == null || presentation.EnterSound == null)
        {
            return;
        }

        AudioManager audioManager = AudioManager.Instance;

        if (audioManager == null || audioManager.Gameplay == null)
        {
            return;
        }

        if (presentation.PlaySoundAtEnemyPosition)
        {
            Transform origin = soundOrigin != null ? soundOrigin : transform;
            audioManager.Gameplay.PlayAtPosition(presentation.EnterSound, origin.position);
            return;
        }

        audioManager.Gameplay.Play2D(presentation.EnterSound);
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
                $"{nameof(EnemyPresentationController)} has no Animator. Enemy SFX and threat presentation will still work.",
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