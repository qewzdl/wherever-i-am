using UnityEngine;

[DisallowMultipleComponent]
public class EnemyNoiseEmitter : MonoBehaviour
{
    [SerializeField, Min(0f)] private float radius = 8f;
    [SerializeField, Min(0f)] private float loudness = 1f;
    [SerializeField] private EnemyTarget sourceTarget;

    public bool RaiseNoiseServer()
    {
        return EnemyNoiseSystem.TryRaiseNoiseServer(
            transform.position,
            radius,
            loudness,
            sourceTarget,
            this
        );
    }

    public bool RaiseNoiseServer(Vector3 position)
    {
        return EnemyNoiseSystem.TryRaiseNoiseServer(
            position,
            radius,
            loudness,
            sourceTarget,
            this
        );
    }

    public bool RaiseNoiseServer(float customRadius, float customLoudness)
    {
        return EnemyNoiseSystem.TryRaiseNoiseServer(
            transform.position,
            customRadius,
            customLoudness,
            sourceTarget,
            this
        );
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        radius = Mathf.Max(0f, radius);
        loudness = Mathf.Max(0f, loudness);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
#endif
}