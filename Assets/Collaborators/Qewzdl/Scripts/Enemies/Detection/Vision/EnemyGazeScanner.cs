using UnityEngine;

// Sweeps the vision cone from side to side while the enemy has nothing to
// chase. EnemyVisionSensor measures its horizontal view angle against the eyes
// transform's forward, so rotating that transform is the whole trick: the
// enemy stops seeing only where it walks and starts looking around.
//
// ponytail: server-only, the swept yaw is not replicated - remote clients see
// the eye meshes facing forward while the host sees them scanning. Add a yaw
// NetworkVariable to EnemyNetworkState if the head turn has to read on screen.
[DisallowMultipleComponent]
public class EnemyGazeScanner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyVisionSensor visionSensor;

    [Header("Sweep")]
    [SerializeField, Range(0f, 120f)] private float sweepAngle = 55f;
    [SerializeField, Min(1f)] private float sweepSpeed = 45f;
    [SerializeField, Min(0f)] private float holdDuration = 0.6f;

    [Tooltip("Randomizes each swing so the sweep does not read as a metronome.")]
    [SerializeField, Range(0f, 1f)] private float sweepVariation = 0.4f;

    [Header("Recenter")]
    [Tooltip("Degrees per second the gaze snaps back onto the body forward once a target is being pursued.")]
    [SerializeField, Min(1f)] private float recenterSpeed = 240f;

    private Transform eyes;
    private Quaternion baseLocalRotation = Quaternion.identity;

    private float currentYaw;
    private float goalYaw;
    private float holdTimer;
    private bool sweepsRight;
    private bool hasEyes;

    public float CurrentYaw => currentYaw;

    public void TickServer(float deltaTime, EnemyState state)
    {
        if (!TryResolveEyes())
        {
            return;
        }

        deltaTime = Mathf.Max(0f, deltaTime);

        if (state == EnemyState.Chase || state == EnemyState.Attack)
        {
            TickRecenter(deltaTime);
        }
        else
        {
            TickSweep(deltaTime);
        }

        ApplyYaw();
    }

    private void TickSweep(float deltaTime)
    {
        if (holdTimer > 0f)
        {
            holdTimer -= deltaTime;
            return;
        }

        if (Mathf.Approximately(currentYaw, goalYaw))
        {
            StartNextSwing();
            return;
        }

        currentYaw = Mathf.MoveTowards(currentYaw, goalYaw, sweepSpeed * deltaTime);
    }

    private void StartNextSwing()
    {
        sweepsRight = !sweepsRight;

        float amplitude = sweepAngle * Random.Range(1f - sweepVariation, 1f);
        goalYaw = sweepsRight ? amplitude : -amplitude;

        holdTimer = holdDuration * Random.Range(1f - sweepVariation, 1f + sweepVariation);
    }

    private void TickRecenter(float deltaTime)
    {
        currentYaw = Mathf.MoveTowards(currentYaw, 0f, recenterSpeed * deltaTime);
        holdTimer = 0f;
    }

    private void ApplyYaw()
    {
        if (eyes == null)
        {
            return;
        }

        eyes.localRotation = baseLocalRotation * Quaternion.Euler(0f, currentYaw, 0f);
    }

    private bool TryResolveEyes()
    {
        if (hasEyes)
        {
            return eyes != null;
        }

        if (visionSensor == null)
        {
            visionSensor = GetComponent<EnemyVisionSensor>();
        }

        Transform resolvedEyes = visionSensor != null ? visionSensor.OriginTransform : null;

        if (resolvedEyes == null)
        {
            return false;
        }

        eyes = resolvedEyes;
        baseLocalRotation = eyes.localRotation;
        hasEyes = true;

        return true;
    }

    private void OnDisable()
    {
        currentYaw = 0f;
        goalYaw = 0f;
        holdTimer = 0f;

        ApplyYaw();
    }
}
