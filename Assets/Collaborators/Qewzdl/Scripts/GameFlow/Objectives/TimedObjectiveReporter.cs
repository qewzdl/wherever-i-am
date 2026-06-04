using UnityEngine;

[DisallowMultipleComponent]
public sealed class TimedObjectiveReporter : ObjectiveReporterBase
{
    [SerializeField] private bool useObjectiveRequiredProgressAsDuration = true;
    [SerializeField] [Min(0.05f)] private float durationSeconds = 10f;
    [SerializeField] [Min(0.05f)] private float progressReportInterval = 0.25f;
    [SerializeField] private bool completeWhenTimerEnds = true;

    private bool timerRunning;
    private float elapsedSeconds;
    private float lastProgressReportTime;

    protected override void OnValidate()
    {
        base.OnValidate();
        durationSeconds = Mathf.Max(0.05f, durationSeconds);
        progressReportInterval = Mathf.Max(0.05f, progressReportInterval);
    }

    private void OnEnable()
    {
        if (!ValidateReporterSetup())
        {
            enabled = false;
        }
    }

    private void OnDisable()
    {
        StopTimer();
    }

    private void Update()
    {
        if (!CanReportObjectiveNow)
        {
            StopTimer();
            return;
        }

        if (!timerRunning)
        {
            StartTimer();
        }

        float duration = GetDurationSeconds();
        elapsedSeconds = Mathf.Min(elapsedSeconds + Time.deltaTime, duration);

        if (elapsedSeconds - lastProgressReportTime >= progressReportInterval || elapsedSeconds >= duration)
        {
            lastProgressReportTime = elapsedSeconds;
            ReportProgress(elapsedSeconds / duration);
        }

        if (elapsedSeconds >= duration)
        {
            if (completeWhenTimerEnds)
            {
                Complete();
            }

            StopTimer();
        }
    }

    private void StartTimer()
    {
        timerRunning = true;
        elapsedSeconds = 0f;
        lastProgressReportTime = 0f;
        ReportProgress(0f);
    }

    private void StopTimer()
    {
        timerRunning = false;
        elapsedSeconds = 0f;
        lastProgressReportTime = 0f;
    }

    private float GetDurationSeconds()
    {
        if (useObjectiveRequiredProgressAsDuration && ObjectiveDefinition != null)
        {
            return Mathf.Max(0.05f, ObjectiveDefinition.RequiredProgress);
        }

        return Mathf.Max(0.05f, durationSeconds);
    }
}
