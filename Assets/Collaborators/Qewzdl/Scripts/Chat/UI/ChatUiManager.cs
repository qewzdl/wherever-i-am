using UnityEngine;

public class ChatUiManager : MonoBehaviour
{
    [Header("UI Mode")]
    [SerializeField] private ChatUiMode mode = ChatUiMode.LobbyWindow;

    [Header("Spawn Root")]
    [SerializeField] private Transform uiRoot;

    [Header("Lobby Chat")]
    [SerializeField] private GameObject lobbyChatPrefab;

    [Header("Phone Chat")]
    [SerializeField] private PhoneChatView phoneChatPrefab;

    [Header("Audio")]
    [SerializeField] private SoundEffect lobbyInputSfx;
    [SerializeField] private SoundEffect phoneInputSfx;

    private GameObject spawnedUi;

    private void Awake()
    {
        SpawnChatUi();
    }

    private void OnDestroy()
    {
        if (spawnedUi == null)
        {
            return;
        }

        Destroy(spawnedUi);
        spawnedUi = null;
    }

    private void SpawnChatUi()
    {
        if (uiRoot == null)
        {
            Debug.LogError($"{nameof(ChatUiManager)}: UI Root is not assigned.", this);
            return;
        }

        switch (mode)
        {
            case ChatUiMode.LobbyWindow:
                SpawnLobbyChat();
                break;

            case ChatUiMode.PhoneWindow:
                SpawnPhoneChat();
                break;
        }
    }

    private void SpawnLobbyChat()
    {
        if (lobbyChatPrefab == null)
        {
            Debug.LogError($"{nameof(ChatUiManager)}: Lobby Chat Prefab is not assigned.", this);
            return;
        }

        spawnedUi = Instantiate(lobbyChatPrefab, uiRoot);
        ApplyChatInputSfx(spawnedUi, lobbyInputSfx);
    }

    private void SpawnPhoneChat()
    {
        if (phoneChatPrefab == null)
        {
            Debug.LogError($"{nameof(ChatUiManager)}: Phone Chat Prefab is not assigned.", this);
            return;
        }

        PhoneChatView phoneView = Instantiate(phoneChatPrefab, uiRoot);
        spawnedUi = phoneView.gameObject;

        StretchToParent(spawnedUi);

        if (phoneInputSfx != null)
        {
            phoneView.SetInputSfx(phoneInputSfx);
        }

        phoneView.Initialize();
    }

    private void ApplyChatInputSfx(GameObject root, SoundEffect inputSfx)
    {
        if (root == null || inputSfx == null)
        {
            return;
        }

        ChatWindowUI chatWindow = root.GetComponentInChildren<ChatWindowUI>(true);

        if (chatWindow == null)
        {
            return;
        }

        chatWindow.SetInputSoundOverride(inputSfx);
    }

    private void StretchToParent(GameObject target)
    {
        RectTransform rectTransform = target.transform as RectTransform;

        if (rectTransform == null)
        {
            return;
        }

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.localScale = Vector3.one;
    }
}
