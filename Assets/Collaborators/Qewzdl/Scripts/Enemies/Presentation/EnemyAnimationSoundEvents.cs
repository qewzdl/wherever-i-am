using UnityEngine;

[DisallowMultipleComponent]
public class EnemyAnimationSoundEvents : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform soundOrigin;

    [Header("Footsteps")]
    [SerializeField] private SoundEffect footstepSound;
    [SerializeField] private SoundEffect chaseFootstepSound;
    [SerializeField] private EnemyNetworkState networkState;

    [Header("Other Animation Sounds")]
    [SerializeField] private SoundEffect breathingSound;
    [SerializeField] private SoundEffect attackSwingSound;

    private void Awake()
    {
        CacheComponents();
    }

    public void PlayFootstep()
    {
        SoundEffect sound = GetFootstepSound();
        PlayAtEnemyPosition(sound);
    }

    public void PlayBreathing()
    {
        PlayAtEnemyPosition(breathingSound);
    }

    public void PlayAttackSwing()
    {
        PlayAtEnemyPosition(attackSwingSound);
    }

    private SoundEffect GetFootstepSound()
    {
        if (networkState != null &&
            networkState.CurrentState == EnemyState.Chase &&
            chaseFootstepSound != null)
        {
            return chaseFootstepSound;
        }

        return footstepSound;
    }

    private void PlayAtEnemyPosition(SoundEffect sound)
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

        Transform origin = soundOrigin != null ? soundOrigin : transform;
        audioManager.Gameplay.PlayAtPosition(sound, origin.position);
    }

    private void CacheComponents()
    {
        if (soundOrigin == null)
        {
            soundOrigin = transform;
        }

        if (networkState == null)
        {
            networkState = GetComponentInParent<EnemyNetworkState>();
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
    }
#endif
}