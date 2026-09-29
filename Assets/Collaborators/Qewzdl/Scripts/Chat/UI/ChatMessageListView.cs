using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ChatMessageListView : MonoBehaviour
{
    [SerializeField] private Transform contentRoot;
    [SerializeField] private ChatMessageItemView itemPrefab;
    [SerializeField] private ScrollRect scrollRect;

    [Tooltip(
        "How far one notch of the mouse wheel moves the messages, in pixels - " +
        "about one line. " +
        "Never more than half the visible height, whatever this says, so no " +
        "line can be stepped over without being seen.")]
    [SerializeField, Min(1f)] private float wheelStep = 24f;

    private readonly Dictionary<uint, ChatMessageItemView> itemsById = new Dictionary<uint, ChatMessageItemView>();
    private readonly List<uint> idsToRemove = new List<uint>();
    private ChatTypographyProfile typographyProfile;

    // The wheel is handled here rather than by the ScrollRect. Its own
    // handling multiplied the UI's per-notch size (6) by the chat's
    // sensitivity (30): a notch moved the messages 180 pixels, several lines
    // at once, and lines in between were never on screen. Scrolling from the
    // input field went a different way again and moved 30.
    private void Awake()
    {
        if (scrollRect == null)
            return;

        scrollRect.scrollSensitivity = 0f;

        if (!scrollRect.TryGetComponent(out ChatWheelScroll wheel))
            wheel = scrollRect.gameObject.AddComponent<ChatWheelScroll>();

        wheel.Bind(this);
    }

    public void SetTypographyProfile(ChatTypographyProfile typographyProfile)
    {
        this.typographyProfile = typographyProfile;
        ChatTypographyApplier.ApplyToTexts(gameObject, typographyProfile);
    }

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

    // Mouse.scroll is one per notch on every platform the input system
    // supports (its uniform range, the project default).
    public void ScrollByWheelDelta(Vector2 scrollDelta)
    {
        ScrollByNotches(scrollDelta.y);
    }

    // Up is positive, towards older messages.
    public void ScrollByNotches(float notches)
    {
        if (scrollRect == null || !scrollRect.vertical || Mathf.Approximately(notches, 0f))
            return;

        RectTransform content = scrollRect.content;
        RectTransform viewport = ResolveScrollRectTransform();

        if (content == null || viewport == null)
            return;

        float overflow = content.rect.height - viewport.rect.height;

        if (overflow <= 0f)
            return;

        float step = Mathf.Min(wheelStep, viewport.rect.height * 0.5f);

        scrollRect.StopMovement();
        scrollRect.verticalNormalizedPosition = Mathf.Clamp01(
            scrollRect.verticalNormalizedPosition + notches * step / overflow);
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

        ChatMessageItemView item = Instantiate(itemPrefab, contentRoot);
        ChatTypographyApplier.ApplyToTexts(item.gameObject, typographyProfile);

        return item;
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
