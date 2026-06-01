using UnityEngine;

[CreateAssetMenu(
    fileName = "TimedObjectiveDefinition",
    menuName = "Wherever I Am/Objectives/Timed Objective Definition")]
public sealed class TimedObjectiveDefinition : ObjectiveDefinition
{
    [Header("Timer")]
    [SerializeField] [Min(0.05f)] private float progressReportInterval = 1f;
    [SerializeField] private bool completeWhenTimerEnds = true;

    public float DurationSeconds => TargetValue;
    public float ProgressReportInterval => Mathf.Max(0.05f, progressReportInterval);
    public bool CompleteWhenTimerEnds => completeWhenTimerEnds;

    protected override void OnValidate()
    {
        base.OnValidate();
        progressReportInterval = Mathf.Max(0.05f, progressReportInterval);
    }
}
