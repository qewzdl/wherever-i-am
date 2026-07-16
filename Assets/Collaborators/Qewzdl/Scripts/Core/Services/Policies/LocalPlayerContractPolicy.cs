using System;

internal sealed class LocalPlayerContractPolicy : ServiceContractPolicy
{
    private static readonly Type[] AllowedContracts =
    {
        typeof(ILocalPlayerInputService),
        typeof(ILocalPlayerCameraService),
        typeof(ILocalPlayerPresentationService)
    };

    internal static readonly LocalPlayerContractPolicy Instance = new();

    private LocalPlayerContractPolicy()
        : base("Local Player", AllowedContracts)
    {
    }

    internal static int AllowedContractCount => Instance.ContractCount;

    internal static bool IsAllowed(Type contractType)
    {
        return Instance.Allows(contractType);
    }
}
