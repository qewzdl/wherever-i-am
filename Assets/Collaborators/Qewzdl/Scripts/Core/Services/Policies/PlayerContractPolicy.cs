using System;

internal sealed class PlayerContractPolicy : ServiceContractPolicy
{
    internal static readonly PlayerContractPolicy Instance = new();

    private PlayerContractPolicy()
        : base("Player", ServiceContractCatalog.Player)
    {
    }

    internal static int AllowedContractCount => Instance.ContractCount;

    internal static bool IsAllowed(Type contractType)
    {
        return Instance.Allows(contractType);
    }
}
