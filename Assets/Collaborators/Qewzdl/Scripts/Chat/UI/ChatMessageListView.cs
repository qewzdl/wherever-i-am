using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
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

    public void ScrollByWheelDelta(Vector2 scrollDelta)
    {
        if (scrollRect == null)
            return;

        if (!scrollRect.vertical)
            return;

        PointerEventData pointerEventData = new PointerEventData(EventSystem.current)
        {
            scrollDelta = scrollDelta
        };

        scrollRect.OnScroll(pointerEventData);
    }

    public bool ContainsScreenPoint(Vector2 screenPosition)
    {
        RectTransform rectTransform = ResolveScrollRectTransform();

        if (rectTransform == null)
            return false;

        return RectTransformUtility.RectangleContainsScreenPoint(
            rectTransform,
            screenPosition,
            ResolveEventCamera(rectTransform));
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

    private RectTransform ResolveScrollRectTransform()
    {
        if (scrollRect != null)
        {
            if (scrollRect.viewport != null)
                return scrollRect.viewport;

            return scrollRect.transform as RectTransform;
        }

        return transform as RectTransform;
    }

    private Camera ResolveEventCamera(RectTransform rectTransform)
    {
        Canvas canvas = rectTransform.GetComponentInParent<Canvas>();

        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;

        return canvas.worldCamera;
    }
}
