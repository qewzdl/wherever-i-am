using UnityEngine;

public class HUDUI : MonoBehaviour, ISettingsServiceConsumer
{
    [SerializeField] private GameObject root;
    private CanvasGroup canvasGroup;
    private ISettingsService settingsService;

    private void Awake()
    {
        ResolveCanvasGroup();
    }

    private void OnEnable()
    {
        ApplySettings();
    }

    public void Construct(ISettingsService settings)
    {
        if (settings == null)
            throw new System.ArgumentNullException(nameof(settings));

        if (ReferenceEquals(settingsService, settings))
            return;

        ReleaseSettingsService();
        settingsService = settings;
        settingsService.SettingsChanged += ApplySettings;
        ResolveCanvasGroup();
        ApplySettings();
    }

    public void ReleaseSettingsService()
    {
        if (settingsService == null)
            return;

        settingsService.SettingsChanged -= ApplySettings;
        settingsService = null;
    }

    private void OnDestroy()
    {
        ReleaseSettingsService();
    }

    private void ResolveCanvasGroup()
    {
        if (canvasGroup == null)
            canvasGroup = (root != null ? root : gameObject).GetComponent<CanvasGroup>();
    }

    private void ApplySettings()
    {
        if (settingsService == null)
            return;

        ResolveCanvasGroup();

        if (canvasGroup != null)
            canvasGroup.alpha = settingsService.Current.interfaceOpacity;
    }

    public void ShowHUD()
    {
        if (root != null)
            root.SetActive(true);
    }

    public void HideHUD()
    {
        if (root != null)
            root.SetActive(false);
    }
}
