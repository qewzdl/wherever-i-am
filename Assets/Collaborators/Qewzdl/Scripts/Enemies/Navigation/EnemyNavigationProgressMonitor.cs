using UnityEngine;

internal sealed class EnemyNavigationProgressMonitor
{
    private bool isTracking;
    private Vector3 samplePosition;
    private float nextSampleTime;
    private float lastProgressTime;
    private float startedTime;

    public void Begin(Vector3 position)
    {
        isTracking = true;
        samplePosition = position;
        startedTime = Time.time;
        lastProgressTime = Time.time;
        nextSampleTime = Time.time;
    }

    public bool IsStuck(
        Vector3 position,
        EnemyNavigationConfig config,
        bool useDirectMovementTimeout)
    {
        if (!isTracking)
        {
            Begin(position);
            return false;
        }

        float now = Time.time;
        float maximumDuration = config != null
            ? Mathf.Max(0.1f, config.directMovementTimeout)
            : 4f;

        if (useDirectMovementTimeout && now - startedTime >= maximumDuration)
        {
            return true;
        }

        if (now < nextSampleTime)
        {
            return false;
        }

        float sampleInterval = config != null
            ? Mathf.Max(0.05f, config.progressSampleInterval)
            : 0.25f;
        float minimumProgress = config != null
            ? Mathf.Max(0.001f, config.minimumProgressDistance)
            : 0.05f;
        float stuckTimeout = config != null
            ? Mathf.Max(0.1f, config.stuckTimeout)
            : 1.5f;

        nextSampleTime = now + sampleInterval;
        Vector3 delta = position - samplePosition;
        delta.y = 0f;

        if (delta.sqrMagnitude >= minimumProgress * minimumProgress)
        {
            samplePosition = position;
            lastProgressTime = now;
            return false;
        }

        return now - lastProgressTime >= stuckTimeout;
    }

    public void Reset()
    {
        isTracking = false;
        samplePosition = default;
        nextSampleTime = 0f;
        lastProgressTime = 0f;
        startedTime = 0f;
    }
}
