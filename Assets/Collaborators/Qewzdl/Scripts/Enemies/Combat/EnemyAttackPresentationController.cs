using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyAttackPresentationController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyAttackNetworkPresenter networkPresenter;
    [SerializeField] private Animator animator;
    [SerializeField] private AudioSource audioSource;

    [Header("Animator Triggers")]
    [SerializeField] private string windupTrigger = "AttackWindup";
    [SerializeField] private string commitTrigger = "AttackCommit";
    [SerializeField] private string recoveryTrigger = "AttackRecovery";
    [SerializeField] private string interruptedTrigger = "AttackInterrupted";
    [SerializeField] private string hitTrigger = "AttackHit";
    [SerializeField] private string missTrigger = "AttackMiss";

    [Header("SFX")]
    [SerializeField] private AudioClip windupSfx;
    [SerializeField] private AudioClip commitSfx;
    [SerializeField] private AudioClip recoverySfx;
    [SerializeField] private AudioClip interruptedSfx;
    [SerializeField] private AudioClip hitSfx;
    [SerializeField] private AudioClip missSfx;

    [Header("VFX")]
    [SerializeField] private ParticleSystem commitVfx;
    [SerializeField] private ParticleSystem hitVfx;
    [SerializeField] private ParticleSystem missVfx;
    [SerializeField] private ParticleSystem interruptedVfx;

    private bool isConfigured;

    private void Awake()
    {
        isConfigured = ValidateReferences();
    }

    private void OnEnable()
    {
        if (!isConfigured)
        {
            return;
        }

        networkPresenter.PhaseReceived += HandlePhaseReceived;
        networkPresenter.ResultReceived += HandleResultReceived;
    }

    private void OnDisable()
    {
        if (!isConfigured)
        {
            return;
        }

        networkPresenter.PhaseReceived -= HandlePhaseReceived;
        networkPresenter.ResultReceived -= HandleResultReceived;
    }

    private bool ValidateReferences()
    {
        if (networkPresenter != null)
        {
            return true;
        }

        Debug.LogError(
            $"{nameof(EnemyAttackPresentationController)} requires explicit {nameof(EnemyAttackNetworkPresenter)} reference.",
            this
        );

        enabled = false;
        return false;
    }

    private void HandlePhaseReceived(EnemyAttackPhaseEvent phaseEvent)
    {
        switch (phaseEvent.Phase)
        {
            case EnemyAttackPhase.AttackWindup:
                TriggerAnimator(windupTrigger);
                PlayOneShot(windupSfx);
                break;

            case EnemyAttackPhase.AttackCommit:
                TriggerAnimator(commitTrigger);
                PlayOneShot(commitSfx);
                PlayVfx(commitVfx);
                break;

            case EnemyAttackPhase.AttackRecovery:
                TriggerAnimator(recoveryTrigger);
                PlayOneShot(recoverySfx);
                break;

            case EnemyAttackPhase.AttackInterrupted:
                TriggerAnimator(interruptedTrigger);
                PlayOneShot(interruptedSfx);
                PlayVfx(interruptedVfx);
                break;
        }
    }

    private void HandleResultReceived(EnemyAttackResult result)
    {
        switch (result.Type)
        {
            case EnemyAttackResultType.Hit:
                TriggerAnimator(hitTrigger);
                PlayOneShot(hitSfx);
                PlayVfx(hitVfx);
                break;

            case EnemyAttackResultType.OutOfRange:
            case EnemyAttackResultType.EffectRejected:
            case EnemyAttackResultType.MissingEffect:
                TriggerAnimator(missTrigger);
                PlayOneShot(missSfx);
                PlayVfx(missVfx);
                break;

            case EnemyAttackResultType.Interrupted:
            case EnemyAttackResultType.InvalidTarget:
                TriggerAnimator(interruptedTrigger);
                PlayOneShot(interruptedSfx);
                PlayVfx(interruptedVfx);
                break;
        }
    }

    private void TriggerAnimator(string triggerName)
    {
        if (animator == null || string.IsNullOrWhiteSpace(triggerName))
        {
            return;
        }

        animator.SetTrigger(triggerName);
    }

    private void PlayOneShot(AudioClip clip)
    {
        if (audioSource == null || clip == null)
        {
            return;
        }

        audioSource.PlayOneShot(clip);
    }

    private static void PlayVfx(ParticleSystem vfx)
    {
        if (vfx == null)
        {
            return;
        }

        vfx.Play(true);
    }
}