using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class LobbyPlayerRow : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button kickButton;

    [Header("Text")]
    [SerializeField] private string ownerStatusText = "Owner";
    [SerializeField] private string readyStatusText = "Ready";
    [SerializeField] private string notReadyStatusText = "Not ready";

    private ulong clientId;

    public event Action<ulong> KickClicked;

    private void Awake()
    {
        if (kickButton != null)
            kickButton.onClick.AddListener(HandleKickClicked);
    }

    private void OnDestroy()
    {
        if (kickButton != null)
            kickButton.onClick.RemoveListener(HandleKickClicked);

        KickClicked = null;
    }

    public void Bind(LobbyPlayerData player, bool isRoomOwner, bool canKick)
    {
        clientId = player.ClientId;

        if (nameText != null)
            nameText.text = player.PlayerName.ToString();

        if (statusText != null)
        {
            statusText.text = isRoomOwner
                ? ownerStatusText
                : player.IsReady
                    ? readyStatusText
                    : notReadyStatusText;
        }

        if (kickButton != null)
            kickButton.gameObject.SetActive(canKick);
    }

    private void HandleKickClicked()
    {
        KickClicked?.Invoke(clientId);
    }
}
