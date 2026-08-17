// What to call a player on screen. The name they chose if the host has one,
// and something serviceable if they did not - a lobby row with an empty label
// is worse than a dull one.
public static class PlayerDisplayName
{
    public static string Resolve(ulong clientId)
    {
        return G.TryResolve(out INetworkSessionAdmissionService admissionService) &&
               admissionService.TryGetPlayerName(clientId, out string playerName)
            ? playerName
            : Fallback(clientId);
    }

    public static string Fallback(ulong clientId)
    {
        return $"Player {clientId}";
    }
}
