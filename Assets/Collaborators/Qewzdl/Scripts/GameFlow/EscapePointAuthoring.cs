using UnityEngine;

[DisallowMultipleComponent]
public class EscapePointAuthoring : MonoBehaviour
{
    [Header("Designer Setup")]
    [SerializeField] private Collider triggerCollider;
    [SerializeField] private bool disableAfterVictory = true;

    [Header("Generated Runtime")]
    [SerializeField] private EscapeVictoryTrigger runtimeTrigger;

    public Collider TriggerCollider => triggerCollider;
    public bool DisableAfterVictory => disableAfterVictory;
    public EscapeVictoryTrigger RuntimeTrigger => runtimeTrigger;

    private void Reset()
    {
        triggerCollider = GetComponent<Collider>();
    }

    private void OnValidate()
    {
        if (triggerCollider == null)
            triggerCollider = GetComponent<Collider>();
    }

    private void OnDrawGizmosSelected()
    {
        if (triggerCollider == null)
            return;

        Gizmos.matrix = triggerCollider.transform.localToWorldMatrix;

        if (triggerCollider is BoxCollider boxCollider)
        {
            Gizmos.DrawWireCube(boxCollider.center, boxCollider.size);
            return;
        }

        if (triggerCollider is SphereCollider sphereCollider)
        {
            Gizmos.DrawWireSphere(sphereCollider.center, sphereCollider.radius);
        }
    }
}