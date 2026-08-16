public enum ObjectiveRuntimeState
{
    None = 0,
    Inactive = 1,
    Active = 2,
    Completed = 3,

    // Gameplay result: the objective was lost (timer ran out, stealth broken,
    // item lost). The sequence decides what happens next.
    Failed = 4,

    // Configuration or invariant error: the flow itself cannot continue and the
    // session is shut down.
    Faulted = 5
}
