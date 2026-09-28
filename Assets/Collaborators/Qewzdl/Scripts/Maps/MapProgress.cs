using System;
using System.Collections.Generic;
using UnityEngine;

// Which maps this player has opened. Maps are played in the catalog's order:
// the first is always open, and each one after it opens once the one before
// it has been won - by this player, in their own game or in somebody else's.
// Progress lives on the machine of whoever played, not in the session.
//
// Kept as the maps won rather than as how far down the list the player got,
// so reordering the catalog or adding a map in the middle moves the gates
// rather than handing out, or taking away, what was earned.
public static class MapProgress
{
    public const string Key = "wia.mapsWon";

    public static bool IsWon(int mapId)
    {
        return Load().Contains(mapId);
    }

    public static void RecordWin(int mapId)
    {
        HashSet<int> won = Load();

        if (won.Add(mapId))
            Save(won);
    }

    public static bool IsUnlocked(IGameMapCatalog catalog, int mapId)
    {
        return IsUnlocked(catalog, mapId, Load());
    }

    // The rule on its own, apart from where the wins are kept.
    public static bool IsUnlocked(IGameMapCatalog catalog, int mapId, ICollection<int> won)
    {
        GameMapDefinition gate = GateOf(catalog, mapId, out bool known);
        return known && (gate == null || won.Contains(gate.MapId));
    }

    // The map that has to be won to open this one; null for the first.
    public static GameMapDefinition GateOf(IGameMapCatalog catalog, int mapId)
    {
        return GateOf(catalog, mapId, out _);
    }

    private static GameMapDefinition GateOf(IGameMapCatalog catalog, int mapId, out bool known)
    {
        known = false;

        if (catalog == null)
            return null;

        for (int i = 0; i < catalog.Count; i++)
        {
            GameMapDefinition map = catalog.GetMapAt(i);

            if (map == null || map.MapId != mapId)
                continue;

            known = true;

            // The nearest map before this one that exists: a hole left in the
            // list is not a gate nobody can pass.
            for (int j = i - 1; j >= 0; j--)
            {
                GameMapDefinition previous = catalog.GetMapAt(j);

                if (previous != null)
                    return previous;
            }

            return null;
        }

        return null;
    }

    private static HashSet<int> Load()
    {
        HashSet<int> won = new();
        string stored = PlayerPrefs.GetString(Key, string.Empty);

        foreach (string part in stored.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out int mapId))
                won.Add(mapId);
        }

        return won;
    }

    private static void Save(HashSet<int> won)
    {
        PlayerPrefs.SetString(Key, string.Join(",", won));
        PlayerPrefs.Save();
    }
}
