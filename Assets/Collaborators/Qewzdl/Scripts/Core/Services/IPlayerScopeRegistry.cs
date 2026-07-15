using System;

public interface IPlayerScope
{
    ulong NetworkObjectId { get; }
    ulong OwnerClientId { get; }
    bool IsLocalPlayer { get; }
    bool IsDisposed { get; }
    IServiceResolver Services { get; }
    IServiceResolver LocalServices { get; }
}

public interface IPlayerScopeRegistry
{
    bool IsDisposed { get; }
    int Count { get; }

    event Action<IPlayerScope> PlayerScopeOpened;
    event Action<IPlayerScope> PlayerScopeClosing;

    bool TryGetPlayerScope(ulong networkObjectId, out IPlayerScope playerScope);
    bool TryGetLocalPlayerScope(out IPlayerScope playerScope);
}
