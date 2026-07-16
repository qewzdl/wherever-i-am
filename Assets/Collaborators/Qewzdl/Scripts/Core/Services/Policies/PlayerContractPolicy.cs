using System;

internal sealed class PlayerContractPolicy : ServiceContractPolicy
{
    private static readonly Type[] AllowedContracts =
    {
        typeof(IPlayerNetworkService),
        typeof(IReplicatedPlayerStateService),
        typeof(IEnemyAttackReceiver)
    };

    internal static readonly PlayerContractPolicy Instance = new();

    private PlayerContractPolicy()
        : base("Player", AllowedContracts)
    {
    }

    internal static int AllowedContractCount => Instance.ContractCount;

    internal static bool IsAllowed(Type contractType)
    {
        return Instance.Allows(contractType);
    }
}
