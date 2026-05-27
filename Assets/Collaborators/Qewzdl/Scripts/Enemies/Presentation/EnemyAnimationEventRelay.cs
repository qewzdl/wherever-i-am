using UnityEngine;

[DisallowMultipleComponent]
public class EnemyAnimationEventRelay : MonoBehaviour
{
    private const string FootstepEventId = "Footstep";
    private const string AttackSwingEventId = "AttackSwing";
    private const string BreathingEventId = "Breathing";

    [SerializeField] private EnemyPresentationController presentationController;

    private void Awake()
    {
        CacheComponents();
    }

    public void PlaySound(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return;
        }

        presentationController?.PlayAnimationSound(eventId);
    }

    public void PlayFootstep()
    {
        PlaySound(FootstepEventId);
    }

    public void PlayAttackSwing()
    {
        PlaySound(AttackSwingEventId);
    }

    public void PlayBreathing()
    {
        PlaySound(BreathingEventId);
    }

    private void CacheComponents()
    {
        if (presentationController == null)
        {
            presentationController = GetComponentInParent<EnemyPresentationController>();
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