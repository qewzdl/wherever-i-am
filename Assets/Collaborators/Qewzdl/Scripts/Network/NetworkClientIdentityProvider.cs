using System;
using UnityEngine;

internal interface INetworkClientIdentityProvider
{
    string GetOrCreatePlayerId();
}

// Who this running game is, for the length of this run.
//
// It used to be stored in persistentDataPath so it survived restarts, which
// made it an identity of the installation rather than of a player: the editor
// and a build on one machine share that folder, so hosting from one and
// joining from the other sent the same id twice and the second was refused as
// a duplicate. Testing on a single PC is the most common thing there is, and
// it could not be done.
//
// Per process is also what the admission registry assumes - it keys one record
// per id, so two connections sharing one id cannot both be represented. What
// this gives up is reclaiming a reserved slot after the process restarts,
// which never fitted inside a twenty second grace period anyway.
internal sealed class NetworkClientIdentityProvider : INetworkClientIdentityProvider
{
    internal const string PlayerIdArgument = "-wiaPlayerId";

    private string cachedPlayerId;

    public string GetOrCreatePlayerId()
    {
        if (!string.IsNullOrEmpty(cachedPlayerId))
            return cachedPlayerId;

        if (TryReadCommandLineOverride(out cachedPlayerId))
            return cachedPlayerId;

        cachedPlayerId = Guid.NewGuid().ToString("N");
        return cachedPlayerId;
    }

    // The acceptance runs give each process an id of their own so a failure
    // names which one it was.
    private static bool TryReadCommandLineOverride(out string playerId)
    {
        string[] arguments = Environment.GetCommandLineArgs();

        for (int i = 0; i < arguments.Length - 1; i++)
        {
            if (!string.Equals(
                    arguments[i],
                    PlayerIdArgument,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (NetworkConnectionPayloadCodec.TryNormalizePlayerId(
                    arguments[i + 1],
                    out playerId))
            {
                return true;
            }

            Debug.LogWarning(
                $"Ignoring '{PlayerIdArgument}' because its value is not a " +
                "player id.");
            break;
        }

        playerId = string.Empty;
        return false;
    }
}
