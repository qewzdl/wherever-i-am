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
        "How strong a noise has to be, where she is standing, for her to run " +
        "to it rather than walk. Strength is the noise's own loudness scaled " +
        "down by distance and age, so it is 'loud or close'. Below this she " +
        "approaches at investigationSearchSpeed. Quiet sounds - breath, a " +
        "walking step, a light knock - top out at 0.35, loud ones start at " +
        "0.65, so 0.4 means a quiet sound never makes her run and a loud one " +
        "does from the nearer half of its range. Losing sight of somebody is " +
        "not a noise and always runs.")]
    [Range(0f, 1f)] public float urgentNoiseScore = 0.4f;

    [Tooltip(
        "Metres the search ring is pushed along the direction the target was " +
        "last facing, so the enemy searches ahead of where it lost them " +
        "rather than evenly around it. Only applies when the last sighting " +
        "was at the place being searched - a noise has no direction. The " +
        "push stops at the first NavMesh edge, so it never reaches through a " +
        "wall. Zero restores the symmetric ring.")]
    [Min(0f)] public float investigationLeadDistance = 1.5f;

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
        urgentNoiseScore = Mathf.Clamp01(urgentNoiseScore);
        investigationLeadDistance = Mathf.Max(0f, investigationLeadDistance);
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