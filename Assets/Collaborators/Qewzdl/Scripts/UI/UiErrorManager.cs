using UnityEngine;

[DisallowMultipleComponent]
public sealed class UiErrorManager : MonoBehaviour, IUiErrorService
{
    private const string DefaultPrefabResourcePath = "UI/UiErrorOverlay";
    private const string DefaultErrorMessage = "Unknown error.";
    private const string MissingManagerWarning = "UiErrorManager was not found. Add UiErrorManager to the Bootstrap scene or the current scene.";

    private static UiErrorManager instance;

    [SerializeField] private UiErrorView errorViewPrefab;

    private UiErrorView errorView;

    public static UiErrorManager Instance
    {
        get
        {
            if (instance != null)
                return instance;

            instance = FindFirstObjectByType<UiErrorManager>();
            return instance;
        }
    }

    public static bool TryGetInstance(out UiErrorManager manager)
    {
        manager = Instance;

        if (manager != null)
            return true;

        Debug.LogWarning(MissingManagerWarning);
        return false;
    }

    public static void Show(string message)
    {
        if (TryGetInstance(out UiErrorManager manager))
            manager.ShowError(message);
    }

    public static void Hide()
    {
        if (TryGetInstance(out UiErrorManager manager))
            manager.HideError();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (errorView != null)
            errorView.CloseRequested -= HideError;

        if (instance == this)
            instance = null;
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
            errorViewPrefab = LoadDefaultPrefab();

        if (errorViewPrefab == null)
        {
            Debug.LogError($"UiErrorManager: prefab with UiErrorView was not found at Resources/{DefaultPrefabResourcePath}.");
            return;
        }

        errorView = Instantiate(errorViewPrefab, transform);
        errorView.name = errorViewPrefab.name;
        errorView.CloseRequested += HideError;
        errorView.Hide();
    }

    private static UiErrorView LoadDefaultPrefab()
    {
        GameObject prefabObject = Resources.Load<GameObject>(DefaultPrefabResourcePath);

        if (prefabObject == null)
            return null;

        if (prefabObject.TryGetComponent(out UiErrorView viewPrefab))
            return viewPrefab;

        return null;
    }

    private static void PlayErrorSound()
    {
        if (AudioManager.Instance != null && AudioManager.Instance.UI != null)
            AudioManager.Instance.UI.PlayError();
    }

    private static void PlayCloseSound()
    {
        if (AudioManager.Instance != null && AudioManager.Instance.UI != null)
            AudioManager.Instance.UI.PlayClose();
    }
}
