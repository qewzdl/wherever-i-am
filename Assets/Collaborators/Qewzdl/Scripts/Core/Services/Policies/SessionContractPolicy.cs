using System;

internal sealed class SessionContractPolicy : ServiceContractPolicy
{
    internal static readonly SessionContractPolicy Instance = new();

    private SessionContractPolicy()
        : base("Session", ServiceContractCatalog.Session)
    {
    }

    internal static int AllowedContractCount => Instance.ContractCount;

    internal static bool IsAllowed(Type contractType)
    {
        return Instance.Allows(contractType);
    }
}
