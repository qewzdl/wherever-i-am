using System;
using UnityEngine;

// The difficulties the game offers: what the lobby lists, what the host picks,
// and the id every enemy looks its own tuning up by.
//
// It holds no enemy config on purpose. It used to be one enemy's catalog doing
// both jobs, which made that enemy the one every other enemy and the lobby
// itself depended on - delete her and the game had no difficulties. What a
// difficulty means for a particular enemy is that enemy's business, in its own
// EnemyDifficultyCatalog, keyed by the ids declared here.
[CreateAssetMenu(
    fileName = "GameDifficultyCatalog",
    menuName = "Wherever I Am/Lobby/Game Difficulty Catalog")]
public sealed class GameDifficultyCatalog : ScriptableObject
{
    [Serializable]
    public struct Difficulty
    {
        [SerializeField] [Min(0)] private int difficultyId;
        [SerializeField] private string displayName;
        [SerializeField] [TextArea(2, 4)] private string description;

        public Difficulty(int difficultyId, string displayName, string description = "")
        {
            this.difficultyId = Mathf.Max(0, difficultyId);
            this.displayName = displayName;
            this.description = description;
        }

        public int DifficultyId => difficultyId;
        public string DisplayName => displayName;
        public string Description => description;
    }

    [SerializeField] [Min(0)] private int defaultDifficultyId;
    [SerializeField] private Difficulty[] difficulties;

    public int DefaultDifficultyId => defaultDifficultyId;
    public int Count => difficulties == null ? 0 : difficulties.Length;

    public bool TryGetAt(int index, out Difficulty difficulty)
    {
        if (difficulties == null || index < 0 || index >= difficulties.Length)
        {
            difficulty = default;
            return false;
        }

        difficulty = difficulties[index];
        return true;
    }

    public bool IsValidDifficultyId(int difficultyId)
    {
        return TryGet(difficultyId, out _);
    }

    public bool TryGet(int difficultyId, out Difficulty difficulty)
    {
        if (difficulties != null)
        {
            foreach (Difficulty candidate in difficulties)
            {
                if (candidate.DifficultyId == difficultyId)
                {
                    difficulty = candidate;
                    return true;
                }
            }
        }

        difficulty = default;
        return false;
    }

    public bool IsValid(out string error)
    {
        if (Count == 0)
        {
            error = $"{nameof(GameDifficultyCatalog)} '{name}' has no difficulties.";
            return false;
        }

        for (int i = 0; i < difficulties.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(difficulties[i].DisplayName))
            {
                error = $"{nameof(GameDifficultyCatalog)} '{name}' has no display name at index {i}.";
                return false;
            }

            for (int j = i + 1; j < difficulties.Length; j++)
            {
                if (difficulties[j].DifficultyId == difficulties[i].DifficultyId)
                {
                    error =
                        $"{nameof(GameDifficultyCatalog)} '{name}' has duplicate difficulty id " +
                        $"{difficulties[i].DifficultyId}.";
                    return false;
                }
            }
        }

        if (!IsValidDifficultyId(defaultDifficultyId))
        {
            error =
                $"{nameof(GameDifficultyCatalog)} '{name}' has no difficulty for default id " +
                $"{defaultDifficultyId}.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void OnValidate()
    {
        defaultDifficultyId = Mathf.Max(0, defaultDifficultyId);
    }
}
