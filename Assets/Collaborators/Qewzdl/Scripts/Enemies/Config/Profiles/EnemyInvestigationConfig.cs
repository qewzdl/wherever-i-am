using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Profiles/Enemy Investigation Config",
    fileName = "EnemyInvestigationConfig"
)]
public class EnemyInvestigationConfig : ScriptableObject
{
    [Min(0f)] public float investigationReachDistance = 0.75f;
    [Min(0.05f)] public float investigationRepathInterval = 0.25f;

    [FormerlySerializedAs("investigationSearchRadius")]
    [Min(0f)] public float investigationBranchRadius = 2.5f;

    [FormerlySerializedAs("investigationSearchPointCount")]
    [Min(0)] public int investigationBranchPointCount = 3;

    [Min(0f)] public float investigationLeafRadius = 1.5f;
    [Min(0)] public int investigationLeafPointCountPerBranch = 3;

    [Min(0f)] public float investigationSearchSpeed = 1.7f;

    [Tooltip(
        "Seconds the enemy stands at each reached point, turning to look " +
        "around by investigationLookAroundAngle. Zero restores the old " +
        "walk-through behaviour and no look around happens at all.")]
    [Min(0f)] public float investigationPointDwellDuration = 1.4f;

    [Tooltip(
        "Degrees the body turns to each side while standing at a point. The " +
        "dwell is split evenly between the two sides, and a turn that does " +
        "not fit simply stops short rather than snapping.")]
    [Range(0f, 120f)] public float investigationLookAroundAngle = 45f;

    [Min(1f)] public float investigationLookAroundSpeed = 140f;

    public void Validate(float stoppingDistance = 0f)
    {
        investigationReachDistance = Mathf.Max(investigationReachDistance, stoppingDistance);
        investigationRepathInterval = Mathf.Max(0.05f, investigationRepathInterval);
        investigationBranchRadius = Mathf.Max(0f, investigationBranchRadius);
        investigationBranchPointCount = Mathf.Max(0, investigationBranchPointCount);
        investigationLeafRadius = Mathf.Max(0f, investigationLeafRadius);
        investigationLeafPointCountPerBranch = Mathf.Max(0, investigationLeafPointCountPerBranch);
        investigationSearchSpeed = Mathf.Max(0f, investigationSearchSpeed);
        investigationPointDwellDuration =
            Mathf.Max(0f, investigationPointDwellDuration);
        investigationLookAroundAngle =
            Mathf.Clamp(investigationLookAroundAngle, 0f, 120f);
        investigationLookAroundSpeed =
            Mathf.Max(1f, investigationLookAroundSpeed);
    }

    private void OnValidate()
    {
        Validate();
    }
}