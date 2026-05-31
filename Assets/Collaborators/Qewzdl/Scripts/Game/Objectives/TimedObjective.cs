using System.Collections;
using UnityEngine;

public sealed class TimedObjective : ObjectiveCondition
{
    [Header("Timer")]
    [SerializeField] private float durationSeconds = 60f;
    [SerializeField] private float progressReportInterval = 1f;
    [SerializeField] private bool completeWhenTimerEnds = true;

    private Coroutine timerCoroutine;
    private float elapsedSeconds;
    private float lastProgressReportTime;

    public override int CurrentValue => Mathf.FloorToInt(elapsedSeconds);
    public override int TargetValue => Mathf.CeilToInt(Mathf.Max(1f, durationSeconds));

    protected override void OnObjectiveStarted()
    {
        elapsedSeconds = 0f;
        lastProgressReportTime = 0f;

        if (timerCoroutine != null)
        {
            StopCoroutine(timerCoroutine);
        }

        timerCoroutine = StartCoroutine(TimerRoutine());
    }

    protected override void OnObjectiveStopped()
    {
        StopTimer();
    }

    protected override void OnObjectiveCompleted()
    {
        StopTimer();
    }

    private IEnumerator TimerRoutine()
    {
        while (elapsedSeconds < durationSeconds)
        {
            yield return null;

            elapsedSeconds += Time.deltaTime;

            if (elapsedSeconds - lastProgressReportTime >= progressReportInterval)
            {
                lastProgressReportTime = elapsedSeconds;
                NotifyProgressChanged();
            }
        }

        elapsedSeconds = durationSeconds;
        NotifyProgressChanged();

        if (completeWhenTimerEnds)
        {
            Complete();
        }

        timerCoroutine = null;
    }

    private void StopTimer()
    {
        if (timerCoroutine == null)
        {
            return;
        }

        StopCoroutine(timerCoroutine);
        timerCoroutine = null;
    }
}