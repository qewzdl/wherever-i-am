using UnityEngine;

public readonly struct EnemyInvestigationSearchPoint
{
    public readonly Vector3 Position;
    public readonly int Depth;
    public readonly int ParentIndex;
    public readonly int BranchIndex;
    public readonly int LocalIndex;

    public EnemyInvestigationSearchPoint(
        Vector3 position,
        int depth,
        int parentIndex,
        int branchIndex,
        int localIndex
    )
    {
        Position = position;
        Depth = depth;
        ParentIndex = parentIndex;
        BranchIndex = branchIndex;
        LocalIndex = localIndex;
    }
}