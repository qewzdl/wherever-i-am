using System;

internal sealed class SceneContractPolicy : ServiceContractPolicy
{
    private static readonly Type[] LobbyContracts =
    {
        typeof(ILobbyReadService),
        typeof(ILobbyCommandService)
    };

    private static readonly Type[] GameContracts =
    {
        typeof(IPauseService)
    };

    internal static readonly SceneContractPolicy MainMenu =
        new("MainMenu Scene", Array.Empty<Type>());

    internal static readonly SceneContractPolicy Lobby =
        new("Lobby Scene", LobbyContracts);

    internal static readonly SceneContractPolicy Game =
        new("Game Scene", GameContracts);

    internal static readonly SceneContractPolicy Map =
        new("Map Scene", Array.Empty<Type>());

    private SceneContractPolicy(string scopeKind, Type[] allowedContracts)
        : base(scopeKind, allowedContracts)
    {
    }

    internal int AllowedContractCount => ContractCount;

    internal bool IsAllowed(Type contractType)
    {
        return Allows(contractType);
    }
}
