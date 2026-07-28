public readonly struct EnemyNavigationQueryTelemetrySnapshot
{
    public int PathQueries { get; }
    public int PositionSamples { get; }
    public int AppliedPaths { get; }
    public int ReusedPaths { get; }
    public int DeferredRepaths { get; }
    public int BudgetRejections { get; }
    public int StuckRecoveries { get; }

    public EnemyNavigationQueryTelemetrySnapshot(
        int pathQueries,
        int positionSamples,
        int appliedPaths,
        int reusedPaths,
        int deferredRepaths,
        int budgetRejections,
        int stuckRecoveries)
    {
        PathQueries = pathQueries;
        PositionSamples = positionSamples;
        AppliedPaths = appliedPaths;
        ReusedPaths = reusedPaths;
        DeferredRepaths = deferredRepaths;
        BudgetRejections = budgetRejections;
        StuckRecoveries = stuckRecoveries;
    }
}

internal sealed class EnemyNavigationQueryTelemetry
{
    private int pathQueries;
    private int positionSamples;
    private int appliedPaths;
    private int reusedPaths;
    private int deferredRepaths;
    private int budgetRejections;
    private int stuckRecoveries;

    public EnemyNavigationQueryTelemetrySnapshot Snapshot => new(
        pathQueries,
        positionSamples,
        appliedPaths,
        reusedPaths,
        deferredRepaths,
        budgetRejections,
        stuckRecoveries);

    public void RecordPathQuery()
    {
        pathQueries++;
    }

    public void RecordPositionSample()
    {
        positionSamples++;
    }

    public void RecordAppliedPath()
    {
        appliedPaths++;
    }

    public void RecordReusedPath()
    {
        reusedPaths++;
    }

    public void RecordDeferredRepath()
    {
        deferredRepaths++;
    }

    public void RecordBudgetRejection()
    {
        budgetRejections++;
    }

    public void RecordStuckRecovery()
    {
        stuckRecoveries++;
    }

    public void Reset()
    {
        pathQueries = 0;
        positionSamples = 0;
        appliedPaths = 0;
        reusedPaths = 0;
        deferredRepaths = 0;
        budgetRejections = 0;
        stuckRecoveries = 0;
    }
}
