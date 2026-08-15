using System;

internal static class NetworkDisconnectReason
{
    private const string TransportEventPrefix = "[Disconnect Event]";

    // Netcode fills DisconnectReason with its own bracketed transport note when
    // the server did not set one. That is diagnostics, not something to put in
    // front of a player, so it counts as no reason at all.
    internal static string UserFacing(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) ||
            reason.StartsWith(TransportEventPrefix, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return reason;
    }
}
