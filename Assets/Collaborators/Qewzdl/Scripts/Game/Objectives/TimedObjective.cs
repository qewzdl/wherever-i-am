using System.Collections;
using UnityEngine;

public sealed class TimedObjective : ObjectiveCondition
{
    [Header("Definition")]
    [SerializeField] private TimedObjectiveDefinition definition;

    private Coroutine timerCoroutine;
    private float elapsedSeconds;
    private float lastProgressReportTime;

    public override ObjectiveDefinition Definition => definition;
    public override int CurrentValue => Mathf.FloorToInt(elapsedSeconds);
    public override int TargetValue => definition != null ? Mathf.CeilToInt(definition.DurationSeconds) : 0;

    protected override void OnObjectiveStarted()
    {
        elapsedSeconds = 0f;
        lastProgressReportTime = 0f;

        StopTimer();

        timerCoroutine = StartCoroutine(TimerRoutine());
    }

    protected override void OnObjectiveCompleted()
    {
        StopTimer();
    }

    protected override void OnObjectiveFailed()
    {
        StopTimer();
    }

    protected override void OnObjectiveCancelled()
    {
        StopTimer();
    }

    private IEnumerator TimerRoutine()
    {
        if (definition == null)
        {
            Debug.LogError($"{nameof(TimedObjective)} requires assigned {nameof(TimedObjectiveDefinition)}.", this);
            yield break;
        }

        while (elapsedSeconds < definition.DurationSeconds)
        {
            yield return null;

            elapsedSeconds += Time.deltaTime;

            if (elapsedSeconds - lastProgressReportTime >= definition.ProgressReportInterval)
            {
                lastProgressReportTime = elapsedSeconds;
                NotifyProgressChanged();
            }
        }

        elapsedSeconds = definition.DurationSeconds;
        NotifyProgressChanged();

        if (definition.CompleteWhenTimerEnds)
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