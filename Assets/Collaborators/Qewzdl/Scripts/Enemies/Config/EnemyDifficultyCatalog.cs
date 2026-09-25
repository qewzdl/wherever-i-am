using System;
using UnityEngine;

// One enemy's tuning for each difficulty the game offers: a difficulty id from
// the GameDifficultyCatalog, and the config this enemy plays it with.
//
// Names, descriptions and which difficulty is the default belong to the game,
// not to an enemy, and live there. Every enemy has one of these and none of
// them is special.
[CreateAssetMenu(
    fileName = "EnemyDifficultyCatalog",
    menuName = "Wherever I Am/Enemies/Enemy Difficulty Catalog")]
public sealed class EnemyDifficultyCatalog : ScriptableObject
{
    [Serializable]
    public struct EnemyDifficultyEntry
    {
        [SerializeField] [Min(0)] private int difficultyId;
        [SerializeField] private EnemyConfig config;

        public EnemyDifficultyEntry(int difficultyId, EnemyConfig config)
        {
            this.difficultyId = Mathf.Max(0, difficultyId);
            this.config = config;
        }

        public int DifficultyId => difficultyId;
        public EnemyConfig Config => config;
    }

    [SerializeField] private EnemyDifficultyEntry[] difficulties;

    public int Count => difficulties == null ? 0 : difficulties.Length;

    public bool TryGetEntryAt(int index, out EnemyDifficultyEntry entry)
    {
        if (difficulties == null || index < 0 || index >= difficulties.Length)
        {
            entry = default;
            return false;
        }

        entry = difficulties[index];
        return true;
    }

    public bool TryGetConfig(int difficultyId, out EnemyConfig config)
    {
        if (difficulties != null)
        {
            for (int i = 0; i < difficulties.Length; i++)
            {
                if (difficulties[i].DifficultyId == difficultyId &&
                    difficulties[i].Config != null)
                {
                    config = difficulties[i].Config;
                    return true;
                }
            }
        }

        config = null;
        return false;
    }

    public bool IsValid(out string error)
    {
        if (difficulties == null || difficulties.Length == 0)
        {
            error = $"{nameof(EnemyDifficultyCatalog)} '{name}' has no difficulties.";
            return false;
        }

        EnemyConfig reference = difficulties[0].Config;

        for (int i = 0; i < difficulties.Length; i++)
        {
            EnemyDifficultyEntry entry = difficulties[i];

            if (entry.Config == null)
            {
                error = $"{nameof(EnemyDifficultyCatalog)} '{name}' has no config at index {i}.";
                return false;
            }

            if (entry.Config.TryGetValidationError(out string configError))
            {
                error = $"{nameof(EnemyDifficultyCatalog)} '{name}' has an invalid config at index {i}: {configError}";
                return false;
            }

            if (!HasSameBodyShape(reference, entry.Config))
            {
                error =
                    $"{nameof(EnemyDifficultyCatalog)} '{name}' difficulty {entry.DifficultyId} describes a " +
                    $"different body than difficulty {difficulties[0].DifficultyId}. Clients keep the collider " +
                    "from the prefab config, so every difficulty must share the posture collider values.";
                return false;
            }

            for (int j = i + 1; j < difficulties.Length; j++)
            {
                if (difficulties[j].DifficultyId == entry.DifficultyId)
                {
                    error = $"{nameof(EnemyDifficultyCatalog)} '{name}' has duplicate difficulty id {entry.DifficultyId}.";
                    return false;
                }
            }
        }

        error = string.Empty;
        return true;
    }

    // Every difficulty the game offers that this enemy has no config for. On
    // such a difficulty it falls back to its prefab's config, so it plays the
    // same on two difficulties and nothing says so.
    public bool CoversEvery(GameDifficultyCatalog game, out string missing)
    {
        missing = string.Empty;

        if (game == null)
        {
            return true;
        }

        for (int i = 0; i < game.Count; i++)
        {
            if (game.TryGetAt(i, out GameDifficultyCatalog.Difficulty difficulty) &&
                !TryGetConfig(difficulty.DifficultyId, out _))
            {
                missing += (missing.Length > 0 ? ", " : string.Empty) + difficulty.DisplayName;
            }
        }

        return missing.Length == 0;
    }

    private static bool HasSameBodyShape(EnemyConfig left, EnemyConfig right)
    {
        return Mathf.Approximately(left.standingBodyColliderHeight, right.standingBodyColliderHeight) &&
               Mathf.Approximately(left.standingBodyColliderRadius, right.standingBodyColliderRadius) &&
               left.standingBodyColliderCenter == right.standingBodyColliderCenter &&
               Mathf.Approximately(left.crawlingBodyColliderHeight, right.crawlingBodyColliderHeight) &&
               Mathf.Approximately(left.crawlingBodyColliderRadius, right.crawlingBodyColliderRadius) &&
               left.crawlingBodyColliderCenter == right.crawlingBodyColliderCenter;
    }
}
