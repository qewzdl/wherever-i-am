using UnityEngine;

[DisallowMultipleComponent]
public class EnemyNoiseEmitter : MonoBehaviour
{
    [SerializeField, Min(0f)] private float radius = 8f;
    [SerializeField, Min(0f)] private float loudness = 1f;
    [SerializeField] private EnemyTarget sourceTarget;
    [SerializeField] private EnemyNoiseWorldService noiseWorldService;

    public void Construct(EnemyNoiseWorldService service)
    {
        noiseWorldService = service;
    }

    public bool RaiseNoiseServer()
    {
        if (!TryGetNoiseWorldService(out EnemyNoiseWorldService service))
        {
            return false;
        }

        return service.TryRaiseNoiseServer(
            transform.position,
            radius,
            loudness,
            sourceTarget,
            this
        );
    }

    public bool RaiseNoiseServer(Vector3 position)
    {
        if (!TryGetNoiseWorldService(out EnemyNoiseWorldService service))
        {
            return false;
        }

        return service.TryRaiseNoiseServer(
            position,
            radius,
            loudness,
            sourceTarget,
            this
        );
    }

    public bool RaiseNoiseServer(float customRadius, float customLoudness)
    {
        if (!TryGetNoiseWorldService(out EnemyNoiseWorldService service))
        {
            return false;
        }

        return service.TryRaiseNoiseServer(
            transform.position,
            customRadius,
            customLoudness,
            sourceTarget,
            this
        );
    }

    private bool TryGetNoiseWorldService(out EnemyNoiseWorldService service)
    {
        if (noiseWorldService == null)
        {
            noiseWorldService = FindFirstObjectByType<EnemyNoiseWorldService>();
        }

        service = noiseWorldService;
        return service != null;
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
