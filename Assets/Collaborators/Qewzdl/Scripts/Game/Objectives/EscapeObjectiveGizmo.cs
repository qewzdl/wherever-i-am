using UnityEngine;

[DisallowMultipleComponent]
public sealed class EscapeObjectiveGizmo : MonoBehaviour
{
    [SerializeField] private Collider triggerCollider;

    private void Reset()
    {
        triggerCollider = GetComponent<Collider>();
    }

    private void OnValidate()
    {
        if (triggerCollider == null)
        {
            triggerCollider = GetComponent<Collider>();
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (triggerCollider == null)
        {
            return;
        }

        Gizmos.matrix = triggerCollider.transform.localToWorldMatrix;

        if (triggerCollider is BoxCollider boxCollider)
        {
            Gizmos.DrawWireCube(boxCollider.center, boxCollider.size);
            return;
        }

        if (triggerCollider is SphereCollider sphereCollider)
        {
            Gizmos.DrawWireSphere(sphereCollider.center, sphereCollider.radius);
            return;
        }

        if (triggerCollider is CapsuleCollider capsuleCollider)
        {
            DrawCapsuleApproximation(capsuleCollider);
        }
    }

    private void DrawCapsuleApproximation(CapsuleCollider capsuleCollider)
    {
        float radius = capsuleCollider.radius;
        float height = Mathf.Max(capsuleCollider.height, radius * 2f);
        Vector3 center = capsuleCollider.center;

        Vector3 size;

        if (capsuleCollider.direction == 0)
        {
            size = new Vector3(height, radius * 2f, radius * 2f);
        }
        else if (capsuleCollider.direction == 1)
        {
            size = new Vector3(radius * 2f, height, radius * 2f);
        }
        else
        {
            size = new Vector3(radius * 2f, radius * 2f, height);
        }

        Gizmos.DrawWireCube(center, size);
    }
}