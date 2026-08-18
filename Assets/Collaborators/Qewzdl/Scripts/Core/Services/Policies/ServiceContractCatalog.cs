using System;

/// <summary>
/// The single source of truth for service contract ownership.
/// Add a new interface contract to exactly the scope that owns its lifetime.
/// Registration remains fail-closed until the contract is declared here.
/// </summary>
internal static class ServiceContractCatalog
{
    internal static readonly Type[] Global =
    {
        typeof(IProjectSceneRegistry),
        typeof(IGameStateService),
        typeof(IProjectSceneFlowService),
        typeof(INetworkSessionService),
        typeof(INetworkSessionAdmissionService),
        typeof(IUiErrorService),
        typeof(ISettingsService),
        typeof(ISettingsScreen),
        typeof(IAudioService),
        typeof(IGameMapCatalog)
    };

    internal static readonly Type[] Session =
    {
        typeof(ISessionServiceRegistry),
        typeof(IPlayerScopeRegistry),
        typeof(IGameMapSessionService),
        typeof(IGameplayNoiseService),
        typeof(IChatReadService),
        typeof(IChatCommandService),
        typeof(ISessionPhaseService),
        typeof(IMatchCompletionService)
    };

    internal static readonly Type[] MainMenuScene = Array.Empty<Type>();

    internal static readonly Type[] LobbyScene =
    {
        typeof(ILobbyReadService),
        typeof(ILobbyCommandService)
    };

    internal static readonly Type[] GameScene =
    {
        typeof(IPauseService)
    };

    internal static readonly Type[] MapScene = Array.Empty<Type>();

    internal static readonly Type[] Player =
    {
        typeof(IPlayerNetworkService),
        typeof(IPlayerActionGate),
        typeof(IReplicatedPlayerStateService),
        typeof(IReplicatedPlayerHidingStateService),
        typeof(IEnemyAttackReceiver)
    };

    internal static readonly Type[] LocalPlayer =
    {
        typeof(IPlayerHidingCommandService),
        typeof(ILocalPlayerInputService),
        typeof(ILocalPlayerCameraService),
        typeof(ILocalPlayerPresentationService)
    };
}
