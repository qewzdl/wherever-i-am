using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Which maps this player has opened. Maps are played in the catalog's order:
// the first is always open, and each one after it opens once the one before
// it has been won - by this player, in their own game or in somebody else's.
// Progress lives on the machine of whoever played, not in the session.
//
// Kept as the maps won rather than as how far down the list the player got,
// so reordering the catalog or adding a map in the middle moves the gates
// rather than handing out, or taking away, what was earned.
//
// Kept in a file of its own, where Steam Cloud can carry it between machines:
// it syncs files, and PlayerPrefs on Windows is the registry. Wins only ever
// accumulate, so two copies that have drifted apart - a game played offline,
// another computer - are made one by taking both: Merge. Nothing earned on
// either is lost, and there is no "which is newer" to get wrong.
public static class MapProgress
{
    // Where progress was kept before it had a file. Read once, into the file.
    public const string LegacyPrefsKey = "wia.mapsWon";

    // The editor keeps its own, as it did in PlayerPrefs: playing a map in the
    // editor does not open it in the game.
#if UNITY_EDITOR
    public const string FileName = "progress-editor.json";
#else
    public const string FileName = "progress.json";
#endif

    public static string FilePath =>
        Path.Combine(Application.persistentDataPath, FileName);

    public static IReadOnlyCollection<int> Won => Load(out _);

    // Takes in wins kept somewhere else - another machine's copy of this
    // file, by way of the cloud. True when that opened anything here.
    public static bool Merge(IEnumerable<int> wins)
    {
        HashSet<int> won = Load(out bool whole);
        int before = won.Count;

        if (wins != null)
            won.UnionWith(wins);

        return won.Count != before && SaveOver(won, whole);
    }

    public static bool IsWon(int mapId)
    {
        return Load(out _).Contains(mapId);
    }

    public static void RecordWin(int mapId)
    {
        HashSet<int> won = Load(out bool whole);

        if (won.Add(mapId))
            SaveOver(won, whole);
    }

    public static bool IsUnlocked(IGameMapCatalog catalog, int mapId)
    {
        return IsUnlocked(catalog, mapId, Load(out _));
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

    // The file's contents, apart from the file, for whatever carries them
    // between machines.
    public static string Serialize(IEnumerable<int> won)
    {
        List<int> sorted = new(won ?? Array.Empty<int>());
        sorted.Sort();
        return JsonUtility.ToJson(new ProgressFile { mapsWon = sorted });
    }

    public static bool TryParse(string text, out HashSet<int> won)
    {
        won = new HashSet<int>();

        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            ProgressFile file = JsonUtility.FromJson<ProgressFile>(text);

            if (file?.mapsWon == null)
                return false;

            won.UnionWith(file.mapsWon);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // whole: what came back is everything this machine has won. It is not
    // when the file is there but could not be opened just now - Steam may be
    // syncing it, a scanner may be reading it - and writing a win over it
    // then would leave that one win and nothing of what was already there.
    private static HashSet<int> Load(out bool whole)
    {
        string path = FilePath;
        whole = true;

        if (TryReadText(path, out string text))
        {
            if (TryParse(text, out HashSet<int> won))
                return won;

            PutAside(path);
        }

        // As it was before the last save: for a file gone bad, or gone.
        if (TryReadText(path + ".bak", out string backup) &&
            TryParse(backup, out HashSet<int> saved))
        {
            return saved;
        }

        if (File.Exists(path))
        {
            whole = false;
            return new HashSet<int>();
        }

        return MigrateFromPrefs();
    }

    private static bool TryReadText(string path, out string text)
    {
        text = null;

        try
        {
            if (!File.Exists(path))
                return false;

            text = File.ReadAllText(path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Put aside rather than written over by the next win, so what it held
    // can still be got back by hand.
    private static void PutAside(string path)
    {
        string aside = path + ".unreadable";

        try
        {
            File.Copy(path, aside, overwrite: true);
            File.Delete(path);
            Debug.LogError($"Map progress in {path} could not be read; it was moved to {aside}.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Debug.LogError($"Map progress in {path} could not be read, nor moved aside: {e.Message}");
        }
    }

    private static HashSet<int> MigrateFromPrefs()
    {
        HashSet<int> won = new();

        if (!PlayerPrefs.HasKey(LegacyPrefsKey))
            return won;

        string stored = PlayerPrefs.GetString(LegacyPrefsKey, string.Empty);

        foreach (string part in stored.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out int mapId))
                won.Add(mapId);
        }

        // Only forgotten there once it is safely here.
        if (Save(won))
        {
            PlayerPrefs.DeleteKey(LegacyPrefsKey);
            PlayerPrefs.Save();
        }

        return won;
    }

    private static bool SaveOver(HashSet<int> won, bool whole)
    {
        if (whole)
            return Save(won);

        Debug.LogError($"Map progress in {FilePath} could not be opened, so it was not saved over.");
        return false;
    }

    // Written beside the file and swapped in, so a crash mid-write leaves the
    // old progress rather than half of the new; the old is kept as .bak. A
    // save that fails says so and lets the game go on - it is called at the
    // end of a match, which must still end.
    private static bool Save(HashSet<int> won)
    {
        string path = FilePath;
        string next = path + ".tmp";
        string backup = path + ".bak";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(next, Serialize(won));

            if (!File.Exists(path))
            {
                File.Move(next, path);
                return true;
            }

            try
            {
                File.Replace(next, path, backup, ignoreMetadataErrors: true);
            }
            catch (Exception e) when (e is PlatformNotSupportedException or IOException)
            {
                File.Copy(path, backup, overwrite: true);
                File.Copy(next, path, overwrite: true);
                File.Delete(next);
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Debug.LogError($"Map progress could not be saved to {path}: {e.Message}");
            return false;
        }
    }

    [Serializable]
    private sealed class ProgressFile
    {
        public int version = 1;
        public List<int> mapsWon = new();
    }
}
