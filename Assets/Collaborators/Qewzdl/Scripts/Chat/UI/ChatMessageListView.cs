using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ChatMessageListView : MonoBehaviour
{
    [SerializeField] private Transform contentRoot;
    [SerializeField] private ChatMessageItemView itemPrefab;
    [SerializeField] private ScrollRect scrollRect;

    private readonly Dictionary<uint, ChatMessageItemView> itemsById = new Dictionary<uint, ChatMessageItemView>();
    private readonly List<uint> idsToRemove = new List<uint>();

    public void Render(IChatReadService readService)
    {
        if (readService == null)
        {
            Clear();
            return;
        }

        ChatChannel currentChannel = readService.CurrentChannel;
        HashSet<uint> visibleMessageIds = new HashSet<uint>();

        int siblingIndex = 0;

        for (int i = 0; i < readService.MessageCount; i++)
        {
            ChatMessageData message = readService.GetMessage(i);

            if (!ShouldShowMessage(message, currentChannel))
                continue;

            visibleMessageIds.Add(message.MessageId);

            if (!itemsById.TryGetValue(message.MessageId, out ChatMessageItemView item))
            {
                item = CreateItem();

                if (item == null)
                    continue;

                itemsById.Add(message.MessageId, item);
            }

            item.SetMessage(message);
            item.transform.SetSiblingIndex(siblingIndex);
            siblingIndex++;
        }

        RemoveHiddenItems(visibleMessageIds);
        ScrollToBottom();
    }

    public void Clear()
    {
        foreach (ChatMessageItemView item in itemsById.Values)
        {
            if (item != null)
                Destroy(item.gameObject);
        }

        itemsById.Clear();
    }

    private ChatMessageItemView CreateItem()
    {
        if (contentRoot == null)
        {
            Debug.LogError("Chat message content root is missing.");
            return null;
        }

        if (itemPrefab == null)
        {
            Debug.LogError("Chat message item prefab is missing.");
            return null;
        }

        return Instantiate(itemPrefab, contentRoot);
    }

    private void RemoveHiddenItems(HashSet<uint> visibleMessageIds)
    {
        idsToRemove.Clear();

        foreach (uint messageId in itemsById.Keys)
        {
            if (!visibleMessageIds.Contains(messageId))
                idsToRemove.Add(messageId);
        }

        for (int i = 0; i < idsToRemove.Count; i++)
        {
            uint messageId = idsToRemove[i];

            if (!itemsById.TryGetValue(messageId, out ChatMessageItemView item))
                continue;

            if (item != null)
                Destroy(item.gameObject);

            itemsById.Remove(messageId);
        }
    }

    private bool ShouldShowMessage(ChatMessageData message, ChatChannel currentChannel)
    {
        if (message.Channel == ChatChannel.System)
            return true;

        return message.Channel == currentChannel;
    }

    private void ScrollToBottom()
    {
        if (scrollRect == null)
            return;

        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f;
    }
}