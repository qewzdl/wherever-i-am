using UnityEngine;

[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Profiles/Enemy Posture Config",
    fileName = "EnemyPostureConfig"
)]
public class EnemyPostureConfig : ScriptableObject
{
    public bool crawlingEnabled = true;

    [Header("Transitions")]
    [Min(0f)] public float standingToCrawlingTransitionDuration = 0.35f;
    [Min(0f)] public float crawlingToStandingTransitionDuration = 0.35f;

    [Header("Agent")]
    [Min(0.1f)] public float standingAgentHeight = 2f;
    [Min(0.05f)] public float standingAgentRadius = 0.35f;
    public float standingAgentBaseOffset = 0f;

    [Min(0.1f)] public float crawlingAgentHeight = 0.75f;
    [Min(0.05f)] public float crawlingAgentRadius = 0.35f;
    public float crawlingAgentBaseOffset = 0f;

    [Min(0.05f)] public float crawlingSpeedMultiplier = 0.55f;
    [Min(0.1f)] public float postureNavMeshSampleRadius = 1.25f;
    [Min(0.05f)] public float postureSwitchSampleRadius = 0.25f;

    [Min(0f)] public float minPostureDuration = 0.75f;
    [Min(0.05f)] public float standingRecoveryCheckInterval = 0.5f;

    [Header("Physics Clearance")]
    public LayerMask standingClearanceMask = ~0;
    [Min(0f)] public float standingClearanceSkin = 0.02f;
    public QueryTriggerInteraction standingClearanceTriggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Body Collider")]
    [Min(0.1f)] public float standingBodyColliderHeight = 2f;
    [Min(0.05f)] public float standingBodyColliderRadius = 0.35f;
    public Vector3 standingBodyColliderCenter = new(0f, 1f, 0f);

    [Min(0.1f)] public float crawlingBodyColliderHeight = 0.75f;
    [Min(0.05f)] public float crawlingBodyColliderRadius = 0.35f;
    public Vector3 crawlingBodyColliderCenter = new(0f, 0.375f, 0f);

    public float GetTransitionDuration(EnemyPosture fromPosture, EnemyPosture toPosture)
    {
        if (fromPosture == toPosture)
        {
            return 0f;
        }

        if (fromPosture == EnemyPosture.Standing && toPosture == EnemyPosture.Crawling)
        {
            return standingToCrawlingTransitionDuration;
        }

        if (fromPosture == EnemyPosture.Crawling && toPosture == EnemyPosture.Standing)
        {
            return crawlingToStandingTransitionDuration;
        }

        Debug.LogError(
            $"{nameof(EnemyPostureConfig)} has no transition duration for {fromPosture} -> {toPosture}.",
            this
        );

        return 0f;
    }

    public void Validate()
    {
        standingToCrawlingTransitionDuration = Mathf.Max(0f, standingToCrawlingTransitionDuration);
        crawlingToStandingTransitionDuration = Mathf.Max(0f, crawlingToStandingTransitionDuration);

        standingAgentHeight = Mathf.Max(0.1f, standingAgentHeight);
        standingAgentRadius = Mathf.Max(0.05f, standingAgentRadius);

        crawlingAgentHeight = Mathf.Max(0.1f, crawlingAgentHeight);
        crawlingAgentRadius = Mathf.Max(0.05f, crawlingAgentRadius);

        crawlingSpeedMultiplier = Mathf.Max(0.05f, crawlingSpeedMultiplier);
        postureNavMeshSampleRadius = Mathf.Max(0.1f, postureNavMeshSampleRadius);
        postureSwitchSampleRadius = Mathf.Max(0.05f, postureSwitchSampleRadius);

        minPostureDuration = Mathf.Max(0f, minPostureDuration);
        standingRecoveryCheckInterval = Mathf.Max(0.05f, standingRecoveryCheckInterval);
        standingClearanceSkin = Mathf.Max(0f, standingClearanceSkin);

        standingBodyColliderHeight = Mathf.Max(0.1f, standingBodyColliderHeight);
        standingBodyColliderRadius = Mathf.Max(0.05f, standingBodyColliderRadius);

        crawlingBodyColliderHeight = Mathf.Max(0.1f, crawlingBodyColliderHeight);
        crawlingBodyColliderRadius = Mathf.Max(0.05f, crawlingBodyColliderRadius);
    }

    private void OnValidate()
    {
        Validate();
    }
}