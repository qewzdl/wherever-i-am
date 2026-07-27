using UnityEngine;

public class HUDUI : MonoBehaviour
{
    [SerializeField] private GameObject root;
    private CanvasGroup canvasGroup;
    private int appliedSettingsRevision = -1;

    private void Awake()
    {
        canvasGroup = (root != null ? root : gameObject).GetComponent<CanvasGroup>();
    }

    private void Update()
    {
        if (canvasGroup == null ||
            !SettingsService.TryGet(out ISettingsService settings) ||
            settings.Revision == appliedSettingsRevision)
            return;

        canvasGroup.alpha = settings.Current.interfaceOpacity;
        appliedSettingsRevision = settings.Revision;
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
