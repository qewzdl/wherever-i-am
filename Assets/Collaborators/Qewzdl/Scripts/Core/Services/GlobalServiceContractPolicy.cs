using System;
using System.Collections.Generic;

internal interface IServiceRegistrationPolicy
{
    void ValidateRegistration(Type contractType, string scopeName);
}

internal sealed class GlobalServiceContractPolicy : IServiceRegistrationPolicy
{
    private static readonly HashSet<Type> AllowedContracts = new()
    {
        typeof(IProjectSceneRegistry),
        typeof(IGameStateService),
        typeof(IProjectSceneFlowService),
        typeof(INetworkSessionService),
        typeof(IUiErrorService),
        typeof(IAudioService),
        typeof(IGameMapCatalog)
    };

    internal static readonly GlobalServiceContractPolicy Instance = new();

    private GlobalServiceContractPolicy()
    {
    }

    internal static int AllowedContractCount => AllowedContracts.Count;

    internal static bool IsAllowed(Type contractType)
    {
        return contractType != null && AllowedContracts.Contains(contractType);
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

    public void ValidateRegistration(Type contractType, string scopeName)
    {
        if (IsAllowed(contractType))
            return;

        throw new InvalidOperationException(
            $"Service contract '{contractType.Name}' is not allowed in Global scope '{scopeName}'.");
    }
}
