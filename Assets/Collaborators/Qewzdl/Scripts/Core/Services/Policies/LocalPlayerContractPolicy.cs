using System;

internal sealed class LocalPlayerContractPolicy : ServiceContractPolicy
{
    internal static readonly LocalPlayerContractPolicy Instance = new();

    private LocalPlayerContractPolicy()
        : base("Local Player", ServiceContractCatalog.LocalPlayer)
    {
    }

    internal static int AllowedContractCount => Instance.ContractCount;

    internal static bool IsAllowed(Type contractType)
    {
        return Instance.Allows(contractType);
    }
}
