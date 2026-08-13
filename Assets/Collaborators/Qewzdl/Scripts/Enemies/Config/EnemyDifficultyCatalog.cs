using System;
using UnityEngine;

// A difficulty is nothing but the EnemyConfig the server hands the enemy, so
// one asset carries the whole list: the lobby validates ids against it, the
// lobby dropdown reads its names, and the session resolves the config.
//
// Only the server ever swaps the config. Clients keep using the one on the
// prefab, which is why every entry has to describe the same body - see
// HasSameBodyShape.
[CreateAssetMenu(
    fileName = "EnemyDifficultyCatalog",
    menuName = "Wherever I Am/Enemies/Enemy Difficulty Catalog")]
public sealed class EnemyDifficultyCatalog : ScriptableObject
{
    [Serializable]
    public struct EnemyDifficultyEntry
    {
        [SerializeField] [Min(0)] private int difficultyId;
        [SerializeField] private string displayName;
        [SerializeField] private EnemyConfig config;

        public EnemyDifficultyEntry(int difficultyId, string displayName, EnemyConfig config)
        {
            this.difficultyId = Mathf.Max(0, difficultyId);
            this.displayName = displayName;
            this.config = config;
        }

        public int DifficultyId => difficultyId;
        public string DisplayName => displayName;
        public EnemyConfig Config => config;
    }

    [SerializeField] [Min(0)] private int defaultDifficultyId;
    [SerializeField] private EnemyDifficultyEntry[] difficulties;

    public int DefaultDifficultyId => defaultDifficultyId;
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

    public bool IsValidDifficultyId(int difficultyId)
    {
        return TryGetConfig(difficultyId, out _);
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

            if (string.IsNullOrWhiteSpace(entry.DisplayName))
            {
                error = $"{nameof(EnemyDifficultyCatalog)} '{name}' has no display name at index {i}.";
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
                    $"{nameof(EnemyDifficultyCatalog)} '{name}' entry '{entry.DisplayName}' describes a " +
                    $"different body than '{difficulties[0].DisplayName}'. Clients keep the collider from " +
                    "the prefab config, so every difficulty must share the posture collider values.";
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

        if (!IsValidDifficultyId(defaultDifficultyId))
        {
            error = $"{nameof(EnemyDifficultyCatalog)} '{name}' has no difficulty for default id {defaultDifficultyId}.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool HasSameBodyShape(EnemyConfig left, EnemyConfig right)
    {
        return left.crawlingEnabled == right.crawlingEnabled &&
               Mathf.Approximately(left.standingBodyColliderHeight, right.standingBodyColliderHeight) &&
               Mathf.Approximately(left.standingBodyColliderRadius, right.standingBodyColliderRadius) &&
               left.standingBodyColliderCenter == right.standingBodyColliderCenter &&
               Mathf.Approximately(left.crawlingBodyColliderHeight, right.crawlingBodyColliderHeight) &&
               Mathf.Approximately(left.crawlingBodyColliderRadius, right.crawlingBodyColliderRadius) &&
               left.crawlingBodyColliderCenter == right.crawlingBodyColliderCenter;
    }

    private void OnValidate()
    {
        defaultDifficultyId = Mathf.Max(0, defaultDifficultyId);
    }
}
