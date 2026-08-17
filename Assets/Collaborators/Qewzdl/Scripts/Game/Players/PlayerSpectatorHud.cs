using System.Collections.Generic;
using TMPro;
using UnityEngine;

// What a caught player is told while watching. Lives in the scene next to the
// rest of the player UI, and reads the spectator view rather than being handed
// to it: that view is added to the player at runtime and cannot be wired to
// anything in the scene.
[DisallowMultipleComponent]
public sealed class PlayerSpectatorHud : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text watchingText;
    [SerializeField] private TMP_Text aliveText;

    [Header("Text")]
    [SerializeField] private string watchingFormat = "Watching {0}";
    [SerializeField] private string nobodyToWatchText = "Nobody left to watch";
    [SerializeField] private string aliveFormat = "{0} still alive";

    private PlayerEnemyAttackReceiver reportedWatched;
    private int reportedAliveCount = -1;
    private bool isVisible = true;

    private void Awake()
    {
        SetVisible(false);
    }

    private void LateUpdate()
    {
        PlayerSpectatorView spectator = PlayerSpectatorView.Current;

        if (spectator == null)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);
        RefreshWatched(spectator.Watched);
        RefreshAliveCount(PlayerEnemyAttackReceiver.All);
    }

    private void SetVisible(bool visible)
    {
        if (isVisible == visible)
            return;

        isVisible = visible;

        if (panel != null)
            panel.SetActive(visible);
    }

    // Only when something actually changed: this runs every frame, and text
    // rewritten every frame is a layout rebuilt every frame.
    private void RefreshWatched(PlayerEnemyAttackReceiver watched)
    {
        if (reportedWatched == watched || watchingText == null)
            return;

        reportedWatched = watched;

        string playerName = ResolveWatchedName(watched);

        watchingText.text = string.IsNullOrEmpty(playerName)
            ? nobodyToWatchText
            : string.Format(watchingFormat, playerName);
    }

    private void RefreshAliveCount(IReadOnlyList<PlayerEnemyAttackReceiver> players)
    {
        int aliveCount = CountAlive(players);

        if (reportedAliveCount == aliveCount || aliveText == null)
            return;

        reportedAliveCount = aliveCount;
        aliveText.text = string.Format(aliveFormat, aliveCount);
    }

    private static int CountAlive(IReadOnlyList<PlayerEnemyAttackReceiver> players)
    {
        if (players == null)
            return 0;

        int aliveCount = 0;

        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] != null && !players[i].IsEliminated)
                aliveCount++;
        }

        return aliveCount;
    }

    private static string ResolveWatchedName(PlayerEnemyAttackReceiver player)
    {
        return player == null ? string.Empty : player.DisplayName;
    }
}
