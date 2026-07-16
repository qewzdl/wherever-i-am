using System;

internal sealed class SceneContractPolicy : ServiceContractPolicy
{
    internal static readonly SceneContractPolicy MainMenu =
        new("MainMenu Scene", ServiceContractCatalog.MainMenuScene);

    internal static readonly SceneContractPolicy Lobby =
        new("Lobby Scene", ServiceContractCatalog.LobbyScene);

    internal static readonly SceneContractPolicy Game =
        new("Game Scene", ServiceContractCatalog.GameScene);

    internal static readonly SceneContractPolicy Map =
        new("Map Scene", ServiceContractCatalog.MapScene);

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
