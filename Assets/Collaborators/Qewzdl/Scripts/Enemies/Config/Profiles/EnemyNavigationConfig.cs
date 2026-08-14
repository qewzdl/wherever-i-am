using UnityEngine;

[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Profiles/Enemy Navigation Config",
    fileName = "EnemyNavigationConfig"
)]
public sealed class EnemyNavigationConfig : ScriptableObject
{
    [Header("Path Planning")]
    [Min(0.05f)] public float repathInterval = 0.2f;
    [Min(0.01f)] public float destinationRepathDistance = 0.3f;
    [Min(0.1f)] public float navMeshSampleRadius = 2f;
    [Min(1)] public int maximumPathQueriesPerRepath = 24;

    [Header("Recovery")]
    [Min(0.05f)] public float progressSampleInterval = 0.25f;
    [Min(0.001f)] public float minimumProgressDistance = 0.05f;
    [Min(0.1f)] public float stuckTimeout = 1.5f;

    [Header("Barricade Shove")]
    [Range(0f, 1f)] public float barricadeShoveChance = 0f;
    [Min(0f)] public float barricadeShoveForce = 8f;
    [Min(0f)] public float barricadeShoveStopDuration = 0.4f;
    [Min(0.1f)] public float barricadeShoveReach = 2.5f;

    public void Validate()
    {
        repathInterval = Mathf.Max(0.05f, repathInterval);
        destinationRepathDistance = Mathf.Max(0.01f, destinationRepathDistance);
        navMeshSampleRadius = Mathf.Max(0.1f, navMeshSampleRadius);
        maximumPathQueriesPerRepath = Mathf.Max(1, maximumPathQueriesPerRepath);
        progressSampleInterval = Mathf.Max(0.05f, progressSampleInterval);
        minimumProgressDistance = Mathf.Max(0.001f, minimumProgressDistance);
        stuckTimeout = Mathf.Max(0.1f, stuckTimeout);
        barricadeShoveChance = Mathf.Clamp01(barricadeShoveChance);
        barricadeShoveForce = Mathf.Max(0f, barricadeShoveForce);
        barricadeShoveStopDuration = Mathf.Max(0f, barricadeShoveStopDuration);
        barricadeShoveReach = Mathf.Max(0.1f, barricadeShoveReach);
    }

    private void OnValidate()
    {
        Validate();
    }
}
