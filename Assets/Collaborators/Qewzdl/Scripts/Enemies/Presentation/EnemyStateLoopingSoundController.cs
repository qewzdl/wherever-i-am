using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyNetworkState))]
public class EnemyStateLoopingSoundController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyNetworkState networkState;
    [SerializeField] private Transform soundOrigin;

    [Header("State Sounds")]
    [SerializeField] private EnemyStateLoopingSound[] loopingSounds;

    private EnemyStateLoopingSound currentLoopingSound;
    private float nextPlayTime;
    private bool subscribedToNetworkState;

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

        if (networkState == null)
        {
            Debug.LogError(
                $"{nameof(EnemyStateLoopingSoundController)} requires {nameof(EnemyNetworkState)}.",
                this
            );

            enabled = false;
            return;
        }

        SubscribeToNetworkState();
        ApplyState(networkState.CurrentState, force: true);
    }

    public override void OnNetworkDespawn()
    {
        Cleanup();
    }

    private void OnDisable()
    {
        Cleanup();
    }

    private void Update()
    {
        if (!IsClient || currentLoopingSound == null || !currentLoopingSound.IsValid)
        {
            return;
        }

        if (Time.time < nextPlayTime)
        {
            return;
        }

        PlayLoopingSound(currentLoopingSound);
        ScheduleNextSound(currentLoopingSound);
    }

    private void HandleStateChanged(EnemyState previousState, EnemyState nextState)
    {
        ApplyState(nextState, force: false);
    }

    private void ApplyState(EnemyState state, bool force)
    {
        if (!force &&
            currentLoopingSound != null &&
            currentLoopingSound.State == state)
        {
            return;
        }

        currentLoopingSound = FindLoopingSound(state);

        if (currentLoopingSound == null || !currentLoopingSound.IsValid)
        {
            nextPlayTime = 0f;
            return;
        }

        if (currentLoopingSound.PlayImmediatelyOnEnter)
        {
            PlayLoopingSound(currentLoopingSound);
        }

        ScheduleNextSound(currentLoopingSound);
    }

    private EnemyStateLoopingSound FindLoopingSound(EnemyState state)
    {
        if (loopingSounds == null)
        {
            return null;
        }

        for (int i = 0; i < loopingSounds.Length; i++)
        {
            EnemyStateLoopingSound loopingSound = loopingSounds[i];

            if (loopingSound == null || loopingSound.State != state)
            {
                continue;
            }

            return loopingSound;
        }

        return null;
    }

    private void PlayLoopingSound(EnemyStateLoopingSound loopingSound)
    {
        if (loopingSound == null || loopingSound.Sound == null)
        {
            return;
        }

        AudioManager audioManager = AudioManager.Instance;

        if (audioManager == null || audioManager.Gameplay == null)
        {
            return;
        }

        if (loopingSound.PlayAtEnemyPosition)
        {
            Transform origin = soundOrigin != null ? soundOrigin : transform;
            audioManager.Gameplay.PlayAtPosition(loopingSound.Sound, origin.position);
            return;
        }

        audioManager.Gameplay.Play2D(loopingSound.Sound);
    }

    private void ScheduleNextSound(EnemyStateLoopingSound loopingSound)
    {
        if (loopingSound == null)
        {
            nextPlayTime = 0f;
            return;
        }

        nextPlayTime = Time.time + loopingSound.GetNextDelay();
    }

    private void SubscribeToNetworkState()
    {
        if (subscribedToNetworkState || networkState == null)
        {
            return;
        }

        networkState.StateChanged += HandleStateChanged;
        subscribedToNetworkState = true;
    }

    private void UnsubscribeFromNetworkState()
    {
        if (!subscribedToNetworkState || networkState == null)
        {
            return;
        }

        networkState.StateChanged -= HandleStateChanged;
        subscribedToNetworkState = false;
    }

    private void Cleanup()
    {
        UnsubscribeFromNetworkState();
        currentLoopingSound = null;
        nextPlayTime = 0f;
    }

    private void CacheComponents()
    {
        if (networkState == null)
        {
            networkState = GetComponent<EnemyNetworkState>();
        }

        if (soundOrigin == null)
        {
            soundOrigin = transform;
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        CacheComponents();

        if (loopingSounds == null)
        {
            return;
        }

        for (int i = 0; i < loopingSounds.Length; i++)
        {
            loopingSounds[i]?.Normalize();
        }
    }
#endif
}