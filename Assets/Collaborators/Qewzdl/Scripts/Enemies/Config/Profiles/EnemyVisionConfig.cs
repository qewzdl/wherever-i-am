
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

    [Tooltip("How often the server refreshes whether any player can see one " +
             "enemy. Samples are staggered between enemies.")]
    [Min(0.05f)] public float stealthVisibilityRefreshInterval = 0.125f;

    [Tooltip("On, the enemy follows the target's real position through walls " +
             "for the memory duration and cannot be shaken off within it - " +
             "the behaviour the chase is balanced around. Off, it holds the " +
             "point where sight broke, so stepping aside behind cover works.")]
    public bool visualMemoryTracksLiveTarget = true;

    // How the enemy sneaks - distances, timeouts, patience - used to sit here
    // too. It describes a decision, not an eye, and it lives in
    // EnemyStealthTacticsConfig now.

    public void Validate()
    {
        detectionRadius = Mathf.Max(0f, detectionRadius);

        horizontalViewAngle = Mathf.Clamp(horizontalViewAngle, 1f, 360f);
        verticalViewAngle = Mathf.Clamp(verticalViewAngle, 1f, 180f);

        loseTargetDistance = Mathf.Max(loseTargetDistance, detectionRadius);
        targetHeightOffset = Mathf.Max(0f, targetHeightOffset);
        targetRefreshInterval = Mathf.Max(0.05f, targetRefreshInterval);
        visualTargetMemoryDuration = Mathf.Max(0f, visualTargetMemoryDuration);
        stealthVisibilityRefreshInterval = Mathf.Max(
            0.05f,
            stealthVisibilityRefreshInterval
        );
    }

    private void OnValidate()
    {
        Validate();
    }
}
