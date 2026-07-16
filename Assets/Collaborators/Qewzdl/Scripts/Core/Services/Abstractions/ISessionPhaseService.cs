internal interface ISessionPhaseService
{
    ProjectSceneKind ServerScenePhase { get; }

    bool TrySetServerScenePhase(ProjectSceneKind sceneKind);
}
