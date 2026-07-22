using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PlayerHidingVignette : MonoBehaviour
{
    private const int TextureResolution = 128;
    private const int CanvasSortingOrder = -1000;
    private const float VisibleEpsilon = 0.001f;

    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private RawImage vignetteImage;
    private Texture2D vignetteTexture;
    private float generatedInnerRadius = -1f;
    private float currentOpacity;
    private float targetOpacity;
    private float fadeSpeed;
    private float lastFadeDuration;

    public bool IsVisible =>
        currentOpacity > VisibleEpsilon || targetOpacity > VisibleEpsilon;
    public float CurrentOpacity => currentOpacity;

    private void Awake()
    {
        enabled = false;
    }

    public void Show(HidingPlaceData settings)
    {
        lastFadeDuration = settings != null
            ? settings.HidingVignetteFadeDuration
            : 0f;

        if (settings == null || !settings.ShowHidingVignette)
        {
            Hide(lastFadeDuration);
            return;
        }

        enabled = true;
        EnsureVisual();
        ApplyAppearance(settings);
        SetTargetOpacity(
            settings.HidingVignetteOpacity,
            settings.HidingVignetteFadeDuration
        );
    }

    public void Hide()
    {
        Hide(lastFadeDuration);
    }

    public void Hide(float fadeDuration)
    {
        if (canvasGroup == null)
        {
            ResetOpacity();
            enabled = false;
            return;
        }

        SetTargetOpacity(0f, fadeDuration);
    }

    public void HideImmediate()
    {
        ResetOpacity();
        enabled = false;
    }

    private void ResetOpacity()
    {
        currentOpacity = 0f;
        targetOpacity = 0f;
        fadeSpeed = 0f;
        ApplyOpacity();
    }

    private void Update()
    {
        if (Mathf.Approximately(currentOpacity, targetOpacity))
        {
            return;
        }

        currentOpacity = fadeSpeed <= 0f
            ? targetOpacity
            : Mathf.MoveTowards(
                currentOpacity,
                targetOpacity,
                fadeSpeed * Time.unscaledDeltaTime
            );
        ApplyOpacity();

        if (!IsVisible)
        {
            enabled = false;
        }
    }

    private void OnDisable()
    {
        ResetOpacity();
    }

    private void OnDestroy()
    {
        if (vignetteTexture == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(vignetteTexture);
        }
        else
        {
            DestroyImmediate(vignetteTexture);
        }

        vignetteTexture = null;
    }

    private void EnsureVisual()
    {
        if (canvasGroup != null && vignetteImage != null)
        {
            return;
        }

        int uiLayer = LayerMask.NameToLayer("UI");
        GameObject canvasObject = new(
            "Local Hiding Vignette",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasGroup)
        );
        canvasObject.transform.SetParent(transform, false);

        if (uiLayer >= 0)
        {
            canvasObject.layer = uiLayer;
        }

        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = CanvasSortingOrder;

        canvasGroup = canvasObject.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        GameObject imageObject = new(
            "Vignette",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(RawImage)
        );
        imageObject.transform.SetParent(canvasObject.transform, false);

        if (uiLayer >= 0)
        {
            imageObject.layer = uiLayer;
        }

        RectTransform imageRect =
            imageObject.GetComponent<RectTransform>();
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = Vector2.zero;
        imageRect.offsetMax = Vector2.zero;

        vignetteImage = imageObject.GetComponent<RawImage>();
        vignetteImage.raycastTarget = false;
        vignetteImage.color = Color.black;
        canvas.enabled = false;
    }

    private void ApplyAppearance(HidingPlaceData settings)
    {
        Color color = settings.HidingVignetteColor;
        color.a = Mathf.Clamp01(color.a);
        vignetteImage.color = color;

        float innerRadius = settings.HidingVignetteInnerRadius;
        if (vignetteTexture != null &&
            Mathf.Approximately(generatedInnerRadius, innerRadius))
        {
            return;
        }

        RebuildTexture(innerRadius);
    }

    private void RebuildTexture(float innerRadius)
    {
        if (vignetteTexture != null)
        {
            Destroy(vignetteTexture);
        }

        vignetteTexture = new Texture2D(
            TextureResolution,
            TextureResolution,
            TextureFormat.RGBA32,
            mipChain: false,
            linear: true
        )
        {
            name = "Runtime Hiding Vignette",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[
            TextureResolution * TextureResolution
        ];
        float radius = Mathf.Clamp(innerRadius, 0f, 0.95f);

        for (int y = 0; y < TextureResolution; y++)
        {
            float normalizedY =
                ((y + 0.5f) / TextureResolution) * 2f - 1f;

            for (int x = 0; x < TextureResolution; x++)
            {
                float normalizedX =
                    ((x + 0.5f) / TextureResolution) * 2f - 1f;
                float distance = Mathf.Sqrt(
                    normalizedX * normalizedX +
                    normalizedY * normalizedY
                );
                float alpha = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(radius, 1f, distance)
                );

                pixels[y * TextureResolution + x] = new Color(
                    1f,
                    1f,
                    1f,
                    alpha
                );
            }
        }

        vignetteTexture.SetPixels32(pixels);
        vignetteTexture.Apply(
            updateMipmaps: false,
            makeNoLongerReadable: true
        );
        vignetteImage.texture = vignetteTexture;
        generatedInnerRadius = radius;
    }

    private void SetTargetOpacity(float opacity, float fadeDuration)
    {
        targetOpacity = Mathf.Clamp01(opacity);
        float duration = Mathf.Max(0f, fadeDuration);

        if (duration <= 0f)
        {
            currentOpacity = targetOpacity;
            fadeSpeed = 0f;
            ApplyOpacity();

            if (!IsVisible)
            {
                enabled = false;
            }

            return;
        }

        fadeSpeed = Mathf.Abs(targetOpacity - currentOpacity) / duration;
        ApplyOpacity();
    }

    private void ApplyOpacity()
    {
        if (canvasGroup == null || canvas == null)
        {
            return;
        }

        canvasGroup.alpha = currentOpacity;
        canvas.enabled = IsVisible;
    }
}
