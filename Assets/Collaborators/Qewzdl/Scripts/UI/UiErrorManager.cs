using UnityEngine;

[DisallowMultipleComponent]
public sealed class UiErrorManager : MonoBehaviour, IUiErrorService
{
    private const string DefaultErrorMessage = "Unknown error.";

    [SerializeField] private UiErrorView errorViewPrefab;

    private UiErrorView errorView;
    // Its own two sounds, and nothing else: handing audio to other components
    // was never this class's business.
    private IUiSoundService uiSoundService;

    private void Awake()
    {
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    public void Construct(IAudioService service)
    {
        uiSoundService = service != null ? service.UI : null;
    }

    public void DisposeComposition()
    {
        uiSoundService = null;
    }

    private void OnDestroy()
    {
        if (errorView != null)
            errorView.CloseRequested -= HideError;

        DisposeComposition();

    }

    public void ShowError(string message)
    {
        EnsureView();

        if (errorView == null)
            return;

        string errorMessage = string.IsNullOrWhiteSpace(message)
            ? DefaultErrorMessage
            : message;

        errorView.Show(errorMessage);
        PlayErrorSound();
    }

    public void HideError()
    {
        if (errorView == null || !errorView.gameObject.activeSelf)
            return;

        errorView.Hide();
        PlayCloseSound();
    }

    private void EnsureView()
    {
        if (errorView != null)
            return;

        if (errorViewPrefab == null)
        {
            Debug.LogError($"{nameof(UiErrorManager)} requires an assigned {nameof(UiErrorView)} prefab.", this);
            return;
        }

        errorView = Instantiate(errorViewPrefab, transform);
        errorView.name = errorViewPrefab.name;

        errorView.CloseRequested += HideError;
        errorView.Hide();
    }

    private void PlayErrorSound()
    {
        uiSoundService?.PlayError();
    }

    private void PlayCloseSound()
    {
        uiSoundService?.PlayClose();
    }
}
