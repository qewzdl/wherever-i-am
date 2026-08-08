
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Profiles/Enemy Vision Config",
    fileName = "EnemyVisionConfig"
)]
public class EnemyVisionConfig : ScriptableObject
{
    [Min(0f)] public float detectionRadius = 12f;

    [FormerlySerializedAs("viewAngle")]
    [Range(1f, 360f)] public float horizontalViewAngle = 180f;

    [Range(1f, 180f)] public float verticalViewAngle = 80f;

    [Min(0f)] public float loseTargetDistance = 16f;
    [Min(0f)] public float targetHeightOffset = 1.2f;
    [Min(0.05f)] public float targetRefreshInterval = 0.25f;
    [Min(0f)] public float visualTargetMemoryDuration = 2f;

    [Header("Stalking")]
    [Tooltip(
        "Closer than this, a seen target is simply chased - there is no room " +
        "to circle round without being watched doing it, and the enemy is " +
        "seconds from reaching it anyway.")]
    [Min(0f)] public float chaseWithoutStalkingDistance = 7.5f;

    [Tooltip(
        "Further than this, a seen target is stalked instead: stop, watch, " +
        "break away and come round behind. Between the two the enemy keeps " +
        "doing whatever it was already doing, so walking near the threshold " +
        "does not flip it back and forth.")]
    [Min(0f)] public float stalkInsteadOfChasingDistance = 10.5f;

    [Tooltip("How long the target has to look at the enemy for it to count " +
             "as having been noticed. A player sweeping the room past it has " +
             "not noticed it.")]
    [Min(0f)] public float stalkNoticedDuration = 0.4f;

    [Tooltip("How long out of sight before a retreat is over.")]
    [Min(0f)] public float retreatBrokenSightDuration = 1.2f;

    [Tooltip("How far the enemy tries to put between itself and the target " +
             "while breaking off.")]
    [Min(1f)] public float retreatDistance = 8f;

    [Tooltip("How far behind the target the enemy tries to come to rest.")]
    [Min(1f)] public float flankBehindDistance = 3.5f;

    [Tooltip("How long to spend going round before giving up and having " +
             "another look from where it stands.")]
    [Min(1f)] public float flankTimeout = 12f;

    [Tooltip("How long to wait behind a target that never turns round.")]
    [Min(1f)] public float ambushPatience = 8f;

    public void Validate()
    {
        detectionRadius = Mathf.Max(0f, detectionRadius);

        horizontalViewAngle = Mathf.Clamp(horizontalViewAngle, 1f, 360f);
        verticalViewAngle = Mathf.Clamp(verticalViewAngle, 1f, 180f);

        loseTargetDistance = Mathf.Max(loseTargetDistance, detectionRadius);
        targetHeightOffset = Mathf.Max(0f, targetHeightOffset);
        targetRefreshInterval = Mathf.Max(0.05f, targetRefreshInterval);
        visualTargetMemoryDuration = Mathf.Max(0f, visualTargetMemoryDuration);

        chaseWithoutStalkingDistance =
            Mathf.Max(0f, chaseWithoutStalkingDistance);

        // The band has to have width. Equal values are a single threshold,
        // and a single threshold flips the enemy between chasing and stalking
        // every time the target drifts across it.
        stalkInsteadOfChasingDistance = Mathf.Max(
            stalkInsteadOfChasingDistance,
            chaseWithoutStalkingDistance + MinimumStalkBand
        );
    }

    private const float MinimumStalkBand = 1f;

    private void OnValidate()
    {
        Validate();
    }
}