using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class RuntimeDebugOverlayController : MonoBehaviour
{
    [Header("Sources")]
    [SerializeField] private RuntimeDebugPanelSource[] sources;

    [Header("Visibility")]
    [SerializeField] private bool showOnStart = true;
    [SerializeField] private bool enableToggleShortcut = true;
    [SerializeField] private Key toggleKey = Key.F3;

    [Header("Refresh")]
    [SerializeField] [Min(0.05f)] private float refreshIntervalSeconds = 0.1f;

    [Header("Layout")]
    [SerializeField] private Vector2 screenOffset = new Vector2(16f, -16f);
    [SerializeField] [Min(320f)] private float panelWidth = 620f;
    [SerializeField] [Min(0f)] private float minPanelHeight = 120f;
    [SerializeField] private Vector2 panelPadding = new Vector2(14f, 12f);
    [SerializeField] private int sortingOrder = 32760;

    [Header("Style")]
    [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.78f);
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] [Min(8f)] private float fontSize = 18f;

    private readonly RuntimeDebugTextBuilder builder = new RuntimeDebugTextBuilder();

    private RuntimeDebugPanelSource[] sortedSources;
    private GameObject canvasObject;
    private RectTransform panelRect;
    private RectTransform textRect;
    private TextMeshProUGUI textComponent;

    private bool isVisible;
    private bool isInitialized;
    private float nextRefreshTime;

    public bool IsVisible => isVisible;

    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!ValidateSetup())
        {
            enabled = false;
            return;
        }

        PrepareSources();
        SubscribeSources();
        EnsureViewCreated();
        SetVisible(showOnStart);

        isInitialized = true;
        RefreshContent();
    }

    private void Update()
    {
        HandleToggleShortcut();

        if (!isInitialized || !isVisible)
        {
            return;
        }

        if (Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        RefreshContent();
    }

    private void OnDisable()
    {
        UnsubscribeSources();
        isInitialized = false;
    }

    private void OnDestroy()
    {
        UnsubscribeSources();

        if (canvasObject == null)
        {
            return;
        }

        Destroy(canvasObject);
        canvasObject = null;
        panelRect = null;
        textRect = null;
        textComponent = null;
    }

    private void OnValidate()
    {
        refreshIntervalSeconds = Mathf.Max(0.05f, refreshIntervalSeconds);
        panelWidth = Mathf.Max(320f, panelWidth);
        minPanelHeight = Mathf.Max(0f, minPanelHeight);
        panelPadding.x = Mathf.Max(0f, panelPadding.x);
        panelPadding.y = Mathf.Max(0f, panelPadding.y);
        fontSize = Mathf.Max(8f, fontSize);
    }

    public void SetVisible(bool visible)
    {
        isVisible = visible;

        if (visible && canvasObject == null && Application.isPlaying)
        {
            EnsureViewCreated();
        }

        if (canvasObject != null)
        {
            canvasObject.SetActive(visible);
        }

        if (isVisible && isInitialized)
        {
            RefreshContent();
        }
    }

    public void RefreshContent()
    {
        if (!isInitialized || textComponent == null)
        {
            return;
        }

        builder.Clear();
        builder.Header("WIAM Runtime Debug");

        builder
            .Row("Toggle", enableToggleShortcut ? toggleKey.ToString() : "Disabled")
            .Row("Visible Sources", CountVisibleSources());

        for (int i = 0; i < sortedSources.Length; i++)
        {
            sortedSources[i].AppendTo(builder);
        }

        string content = builder.Build();
        textComponent.text = content;
        UpdatePanelHeight(content);

        nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
    }

    private bool ValidateSetup()
    {
        if (sources == null || sources.Length == 0)
        {
            Debug.LogError($"{nameof(RuntimeDebugOverlayController)} requires at least one assigned debug source.", this);
            return false;
        }

        for (int i = 0; i < sources.Length; i++)
        {
            RuntimeDebugPanelSource source = sources[i];

            if (source == null)
            {
                Debug.LogError($"{nameof(RuntimeDebugOverlayController)} has null source at index {i}.", this);
                return false;
            }

            if (!source.IsValidSource(out string error))
            {
                Debug.LogError(
                    $"{nameof(RuntimeDebugOverlayController)} source '{source.name}' is invalid: {error}",
                    source);

                return false;
            }
        }

        return true;
    }

    private void PrepareSources()
    {
        sortedSources = new RuntimeDebugPanelSource[sources.Length];
        Array.Copy(sources, sortedSources, sources.Length);

        Array.Sort(
            sortedSources,
            (left, right) => left.Order.CompareTo(right.Order));
    }

    private void SubscribeSources()
    {
        for (int i = 0; i < sources.Length; i++)
        {
            sources[i].Changed += HandleSourceChanged;
        }
    }

    private void UnsubscribeSources()
    {
        if (sources == null)
        {
            return;
        }

        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] == null)
            {
                continue;
            }

            sources[i].Changed -= HandleSourceChanged;
        }
    }

    private void HandleSourceChanged()
    {
        if (!isInitialized || !isVisible)
        {
            return;
        }

        RefreshContent();
    }

    private void HandleToggleShortcut()
    {
        if (!enableToggleShortcut)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            return;
        }

        if (!keyboard[toggleKey].wasPressedThisFrame)
        {
            return;
        }

        SetVisible(!isVisible);
    }

    private void EnsureViewCreated()
    {
        if (canvasObject != null && panelRect != null && textRect != null && textComponent != null)
        {
            return;
        }

        canvasObject = new GameObject("Runtime Debug Overlay Canvas", typeof(RectTransform));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler canvasScaler = canvasObject.AddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasScaler.matchWidthOrHeight = 0.5f;

        GameObject panelObject = new GameObject("Panel", typeof(RectTransform));
        panelObject.transform.SetParent(canvasObject.transform, false);

        panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = screenOffset;
        panelRect.sizeDelta = new Vector2(panelWidth, minPanelHeight);

        Image panelImage = panelObject.AddComponent<Image>();
        panelImage.color = backgroundColor;
        panelImage.raycastTarget = false;

        GameObject textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(panelObject.transform, false);

        textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0f, 1f);
        ApplyTextOffsets();

        textComponent = textObject.AddComponent<TextMeshProUGUI>();
        textComponent.raycastTarget = false;
        textComponent.color = textColor;
        textComponent.fontSize = fontSize;
        textComponent.alignment = TextAlignmentOptions.TopLeft;
        textComponent.enableWordWrapping = false;
        textComponent.overflowMode = TextOverflowModes.Overflow;
        textComponent.text = string.Empty;
    }

    private void UpdatePanelHeight(string content)
    {
        float textWidth = Mathf.Max(1f, panelWidth - panelPadding.x * 2f);
        float preferredTextHeight = textComponent.GetPreferredValues(content, textWidth, 0f).y;
        float panelHeight = Mathf.Max(minPanelHeight, preferredTextHeight + panelPadding.y * 2f);

        panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);
        ApplyTextOffsets();
    }

    private void ApplyTextOffsets()
    {
        if (textRect == null)
        {
            return;
        }

        textRect.offsetMin = new Vector2(panelPadding.x, panelPadding.y);
        textRect.offsetMax = new Vector2(-panelPadding.x, -panelPadding.y);
    }

    private int CountVisibleSources()
    {
        if (sortedSources == null)
        {
            return 0;
        }

        int count = 0;

        for (int i = 0; i < sortedSources.Length; i++)
        {
            if (sortedSources[i].IsVisible)
            {
                count++;
            }
        }

        return count;
    }
}