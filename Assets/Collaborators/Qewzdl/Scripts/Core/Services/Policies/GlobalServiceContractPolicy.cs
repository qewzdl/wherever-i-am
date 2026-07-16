using System;

internal sealed class GlobalServiceContractPolicy : ServiceContractPolicy
{
    internal static readonly GlobalServiceContractPolicy Instance = new();

    private GlobalServiceContractPolicy()
        : base("Global", ServiceContractCatalog.Global)
    {
    }

    internal static int AllowedContractCount => Instance.ContractCount;

    internal static bool IsAllowed(Type contractType)
    {
        return Instance.Allows(contractType);
    }

    internal static void ValidatePublicAccess(Type contractType)
    {
        if (IsAllowed(contractType))
            return;

        string contractName = contractType != null
            ? contractType.Name
            : "Missing";

        throw new InvalidOperationException(
            $"Service contract '{contractName}' is not a public Global contract and cannot be resolved through {nameof(G)}.");
    }

}
