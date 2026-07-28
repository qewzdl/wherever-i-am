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

    [Header("Dynamic Traversal")]
    [Min(0.05f)] public float directPathCheckInterval = 0.2f;

    [Header("Recovery")]
    [Min(0.05f)] public float progressSampleInterval = 0.25f;
    [Min(0.001f)] public float minimumProgressDistance = 0.05f;
    [Min(0.1f)] public float stuckTimeout = 1.5f;
    [Min(0.1f)] public float directMovementTimeout = 4f;

    public void Validate()
    {
        repathInterval = Mathf.Max(0.05f, repathInterval);
        destinationRepathDistance = Mathf.Max(0.01f, destinationRepathDistance);
        navMeshSampleRadius = Mathf.Max(0.1f, navMeshSampleRadius);
        maximumPathQueriesPerRepath = Mathf.Max(1, maximumPathQueriesPerRepath);
        directPathCheckInterval = Mathf.Max(0.05f, directPathCheckInterval);
        progressSampleInterval = Mathf.Max(0.05f, progressSampleInterval);
        minimumProgressDistance = Mathf.Max(0.001f, minimumProgressDistance);
        stuckTimeout = Mathf.Max(0.1f, stuckTimeout);
        directMovementTimeout = Mathf.Max(0.1f, directMovementTimeout);
    }

    private void OnValidate()
    {
        Validate();
    }
}
