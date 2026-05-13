using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PhoneScreenLayoutController : MonoBehaviour
{
    private const string PhoneRootName = "PhoneRoot";
    private const string PhoneImageName = "PhoneImage";
    private const string ScreenName = "Screen";
    private const string ChatContainerName = "ChatContainer";

    [Header("References")]
    [SerializeField] private RectTransform phoneRoot;
    [SerializeField] private Image phoneImage;
    [SerializeField] private RectTransform screenRect;
    [SerializeField] private RectTransform chatContainer;

    [Header("Layout")]
    [SerializeField] private bool fitScreenRectToTexture = true;
    [SerializeField] private Rect screenPixelRectFromTopLeft = new Rect(310f, 1120f, 1278f, 920f);
    [SerializeField] private Vector4 chatContainerPadding = new Vector4(8f, 8f, 8f, 8f);
    [SerializeField] private bool addScreenRectMask = true;

    public RectTransform PhoneRoot => phoneRoot;
    public Image PhoneImage => phoneImage;
    public RectTransform ScreenRect => screenRect;
    public RectTransform ChatContainer => chatContainer;

    public void ConfigureReferences(
        RectTransform phoneRoot,
        Image phoneImage,
        RectTransform screenRect,
        RectTransform chatContainer)
    {
        this.phoneRoot = phoneRoot;
        this.phoneImage = phoneImage;
        this.screenRect = screenRect;
        this.chatContainer = chatContainer;
    }

    public void ConfigureLayout(
        bool fitScreenRectToTexture,
        Rect screenPixelRectFromTopLeft,
        Vector4 chatContainerPadding,
        bool addScreenRectMask)
    {
        this.fitScreenRectToTexture = fitScreenRectToTexture;
        this.screenPixelRectFromTopLeft = screenPixelRectFromTopLeft;
        this.chatContainerPadding = chatContainerPadding;
        this.addScreenRectMask = addScreenRectMask;
    }

    public void ResolveReferences()
    {
        if (phoneRoot == null)
        {
            phoneRoot = FindChildRectTransform(PhoneRootName);
        }

        if (phoneRoot == null)
        {
            phoneRoot = transform as RectTransform;
        }

        if (phoneImage == null && phoneRoot != null)
        {
            phoneImage = phoneRoot.GetComponent<Image>();
        }

        if (phoneImage == null)
        {
            phoneImage = FindChildImage(PhoneImageName);
        }

        if (chatContainer == null)
        {
            chatContainer = FindChildRectTransform(ChatContainerName);
        }

        if (screenRect == null)
        {
            screenRect = FindChildRectTransform(ScreenName);
        }

        if (screenRect == null && chatContainer != null)
        {
            screenRect = chatContainer.parent as RectTransform;
        }
    }

    public void Apply(bool ensureMask)
    {
        if (!fitScreenRectToTexture)
        {
            ApplyChatContainerPadding();
            return;
        }

        ResolveReferences();

        if (phoneImage == null || screenRect == null)
        {
            ApplyChatContainerPadding();
            return;
        }

        if (!TryGetTextureRect(out Rect textureRect))
        {
            ApplyChatContainerPadding();
            return;
        }

        float textureWidth = Mathf.Max(1f, textureRect.width);
        float textureHeight = Mathf.Max(1f, textureRect.height);
        float left = Mathf.Clamp(screenPixelRectFromTopLeft.xMin, 0f, textureWidth);
        float top = Mathf.Clamp(screenPixelRectFromTopLeft.yMin, 0f, textureHeight);
        float right = Mathf.Clamp(screenPixelRectFromTopLeft.xMax, left, textureWidth);
        float bottom = Mathf.Clamp(screenPixelRectFromTopLeft.yMax, top, textureHeight);

        if (right <= left || bottom <= top)
        {
            ApplyChatContainerPadding();
            return;
        }

        screenRect.anchorMin = new Vector2(left / textureWidth, 1f - bottom / textureHeight);
        screenRect.anchorMax = new Vector2(right / textureWidth, 1f - top / textureHeight);
        screenRect.anchoredPosition = Vector2.zero;
        screenRect.offsetMin = Vector2.zero;
        screenRect.offsetMax = Vector2.zero;
        screenRect.localScale = Vector3.one;

        ApplyChatContainerPadding();

        if (ensureMask && addScreenRectMask && screenRect.GetComponent<RectMask2D>() == null)
        {
            screenRect.gameObject.AddComponent<RectMask2D>();
        }
    }

    private bool TryGetTextureRect(out Rect textureRect)
    {
        if (phoneImage.sprite != null)
        {
            textureRect = phoneImage.sprite.rect;
            return true;
        }

        if (phoneImage.mainTexture != null)
        {
            textureRect = new Rect(
                0f,
                0f,
                phoneImage.mainTexture.width,
                phoneImage.mainTexture.height);
            return true;
        }

        textureRect = Rect.zero;
        return false;
    }

    private void ApplyChatContainerPadding()
    {
        if (chatContainer == null)
        {
            return;
        }

        chatContainer.anchorMin = Vector2.zero;
        chatContainer.anchorMax = Vector2.one;
        chatContainer.offsetMin = new Vector2(chatContainerPadding.x, chatContainerPadding.w);
        chatContainer.offsetMax = new Vector2(-chatContainerPadding.z, -chatContainerPadding.y);
        chatContainer.localScale = Vector3.one;
    }

    private RectTransform FindChildRectTransform(string childName)
    {
        RectTransform[] rectTransforms = GetComponentsInChildren<RectTransform>(true);

        for (int i = 0; i < rectTransforms.Length; i++)
        {
            if (rectTransforms[i].name == childName)
            {
                return rectTransforms[i];
            }
        }

        return null;
    }

    private Image FindChildImage(string childName)
    {
        Image[] images = GetComponentsInChildren<Image>(true);

        for (int i = 0; i < images.Length; i++)
        {
            if (images[i].name == childName)
            {
                return images[i];
            }
        }

        return null;
    }
}
