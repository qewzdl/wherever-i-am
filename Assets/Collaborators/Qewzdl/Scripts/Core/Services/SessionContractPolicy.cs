using System;

internal sealed class SessionContractPolicy : ServiceContractPolicy
{
    private static readonly Type[] AllowedContracts =
    {
        typeof(ISessionServiceRegistry),
        typeof(IPlayerScopeRegistry),
        typeof(IGameMapSessionService),
        typeof(IGameplayNoiseService),
        typeof(IChatReadService),
        typeof(IChatCommandService)
    };

    internal static readonly SessionContractPolicy Instance = new();

    private SessionContractPolicy()
        : base("Session", AllowedContracts)
    {
    }

    internal static int AllowedContractCount => Instance.ContractCount;

    internal static bool IsAllowed(Type contractType)
    {
        return Instance.Allows(contractType);
    }
}
