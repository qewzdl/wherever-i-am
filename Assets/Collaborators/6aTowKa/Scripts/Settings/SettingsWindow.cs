using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Сценовый экземпляр prefab SettingsWindow. Иерархия и ссылки хранятся в prefab;
/// этот класс только связывает уже существующие контролы с SettingsService.
/// </summary>
[DisallowMultipleComponent]
public sealed class SettingsWindow : MonoBehaviour, ISettingsServiceConsumer
{
    [Header("Prefab references")]
    [SerializeField] private GameObject overlay;
    [SerializeField] private GameObject confirmationPanel;
    [SerializeField] private TMP_Text confirmationText;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button applyButton;
    [SerializeField] private Button defaultsButton;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelDialogButton;
    [SerializeField] private Button[] tabButtons;
    [SerializeField] private GameObject[] tabPages;
    [SerializeField] private string[] tabIds;

    [Header("Graphics")]
    [SerializeField] private Button previousResolutionButton;
    [SerializeField] private Button nextResolutionButton;
    [SerializeField] private TMP_Text resolutionText;
    [SerializeField] private Button displayModeButton;
    [SerializeField] private TMP_Text displayModeText;
    [SerializeField] private Button previousQualityButton;
    [SerializeField] private Button nextQualityButton;
    [SerializeField] private TMP_Text qualityText;
    [SerializeField] private Button vsyncButton;
    [SerializeField] private TMP_Text vsyncText;
    [SerializeField] private TMP_Dropdown frameRateDropdown;
    [SerializeField] private Slider fovSlider;
    [SerializeField] private Button smoothingButton;
    [SerializeField] private TMP_Text smoothingText;
    [SerializeField] private Slider smoothingIntensitySlider;

    [Header("Sound")]
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider effectsSlider;

    [Header("Controls")]
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private Button invertButton;
    [SerializeField] private TMP_Text invertText;

    [Header("Interface")]
    [SerializeField] private Slider interfaceSlider;
    [SerializeField] private Slider interfaceOpacitySlider;
    [SerializeField] private Slider crosshairSizeSlider;

    private ISettingsService service;
    private ISettingsEditSession session;
    private string selectedTab = "Graphics";
    private bool showingDefaultsConfirmation;

    /// <summary>Подпись строки служит и заголовком, и индикатором: «Музыка: 75%».</summary>
    private readonly List<(Slider slider, TMP_Text label, string title, Func<float, string> format)> sliderValues = new();

    private void Awake()
    {
        if (overlay == null || tabPages == null || tabPages.Length == 0)
            Debug.LogError("SettingsWindow prefab references are incomplete.", this);
        else
            overlay.SetActive(false);

        // Диапазон задан в коде и в Sanitize, поэтому prefab не должен с ним расходиться.
        if (sensitivitySlider != null)
        {
            sensitivitySlider.minValue = GameSettingsData.MinMouseSensitivity;
            sensitivitySlider.maxValue = GameSettingsData.MaxMouseSensitivity;
        }

        if (frameRateDropdown != null)
        {
            List<string> labels = new(GameSettingsData.FrameRateLimits.Length);
            foreach (int limit in GameSettingsData.FrameRateLimits)
                labels.Add(limit < 0 ? "Unlimited" : $"{limit} FPS");

            frameRateDropdown.ClearOptions();
            frameRateDropdown.AddOptions(labels);
        }

        BindSliderValue(masterSlider, Percent);
        BindSliderValue(musicSlider, Percent);
        BindSliderValue(effectsSlider, Percent);
        BindSliderValue(interfaceSlider, Percent);
        BindSliderValue(interfaceOpacitySlider, Percent);
        BindSliderValue(smoothingIntensitySlider, Percent);
        BindSliderValue(sensitivitySlider, Whole);
        BindSliderValue(fovSlider, Whole);
        BindSliderValue(crosshairSizeSlider, Multiplier);
        UpdateSliderValues();
    }

    private static string Percent(float value) => $"{Mathf.RoundToInt(value * 100f)}%";

    private static string Whole(float value) => Mathf.RoundToInt(value).ToString();

    private static string Multiplier(float value) => $"×{value:0.0}";

    /// <summary>
    /// Заголовок запоминается один раз: если перечитывать его после дописывания значения,
    /// подпись начнёт наращивать «Музыка: 75%: 80%».
    /// </summary>
    private void BindSliderValue(Slider slider, Func<float, string> format)
    {
        if (slider == null || slider.transform.parent == null)
            return;

        // Окно свёрнуто на старте, поэтому неактивные объекты тоже нужно просматривать.
        TMP_Text label = slider.transform.parent.GetComponentInChildren<TMP_Text>(true);

        if (label != null)
            sliderValues.Add((slider, label, label.text, format));
    }

    private void UpdateSliderValues()
    {
        for (int i = 0; i < sliderValues.Count; i++)
        {
            var entry = sliderValues[i];
            entry.label.text = $"{entry.title}: {entry.format(entry.slider.value)}";
        }
    }

    private void OnEnable()
    {
        AttachListeners();
    }

    private void OnDisable()
    {
        DetachListeners();
        CancelEdit();
    }

    private void Update()
    {
        if (overlay == null || !overlay.activeSelf)
            return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Close();
            return;
        }

        if (service != null && service.IsDisplayConfirmationPending && !showingDefaultsConfirmation)
        {
            confirmationPanel.SetActive(true);
            confirmationText.text = $"Keep the new resolution? {Mathf.CeilToInt(service.DisplayConfirmationRemaining)} s";
        }
    }

    private void OnDestroy()
    {
        ReleaseSettingsService();
    }

    public void Construct(ISettingsService settingsService)
    {
        if (settingsService == null)
            throw new ArgumentNullException(nameof(settingsService));

        if (ReferenceEquals(service, settingsService))
            return;

        ReleaseSettingsService();
        service = settingsService;
    }

    public void ReleaseSettingsService()
    {
        if (service != null && service.IsDisplayConfirmationPending)
            service.RevertDisplayChanges();

        CancelEdit();
        service = null;
    }

    public void Open()
    {
        if (overlay != null && overlay.activeSelf)
            return;

        if (service == null)
        {
            Debug.LogWarning("Settings service is not ready.", this);
            return;
        }

        CancelEdit();
        session = service.BeginEdit();
        RefreshFromDraft();
        SelectTab(selectedTab);
        overlay.SetActive(true);
    }

    public void Close()
    {
        if (service != null && service.IsDisplayConfirmationPending)
            service.RevertDisplayChanges();

        CancelEdit();
        showingDefaultsConfirmation = false;
        if (confirmationPanel != null)
            confirmationPanel.SetActive(false);
        if (overlay != null)
            overlay.SetActive(false);
    }

    private void Apply()
    {
        if (session == null || session.IsCompleted)
            return;

        session.Apply();
        session = service.BeginEdit();
        RefreshFromDraft();
        if (service.IsDisplayConfirmationPending)
        {
            showingDefaultsConfirmation = false;
            confirmationText.text = $"Keep the new resolution? {Mathf.CeilToInt(service.DisplayConfirmationRemaining)} s";
            confirmationPanel.SetActive(true);
        }
    }

    private void AskForDefaults()
    {
        showingDefaultsConfirmation = true;
        confirmationText.text = "Reset all settings to defaults?";
        confirmationPanel.SetActive(true);
    }

    private void ConfirmDialog()
    {
        if (showingDefaultsConfirmation)
        {
            session?.ResetToDefaults();
            session?.Apply();
            session = service.BeginEdit();
            RefreshFromDraft();
            showingDefaultsConfirmation = false;
            if (!service.IsDisplayConfirmationPending)
                confirmationPanel.SetActive(false);
            return;
        }

        service?.ConfirmDisplayChanges();
        confirmationPanel.SetActive(false);
    }

    private void CancelDialog()
    {
        if (!showingDefaultsConfirmation)
        {
            service?.RevertDisplayChanges();
            CancelEdit();
            session = service?.BeginEdit();
        }

        showingDefaultsConfirmation = false;
        confirmationPanel.SetActive(false);
        RefreshFromDraft();
    }

    private void ChangeResolution(int direction)
    {
        if (session == null)
            return;
        Resolution[] resolutions = Screen.resolutions;
        if (resolutions.Length == 0)
            return;

        int selected = 0;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < resolutions.Length; i++)
        {
            int distance = Mathf.Abs(resolutions[i].width - session.Draft.resolutionWidth) +
                           Mathf.Abs(resolutions[i].height - session.Draft.resolutionHeight);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                selected = i;
            }
        }

        int currentWidth = resolutions[selected].width;
        int currentHeight = resolutions[selected].height;
        for (int step = 1; step < resolutions.Length; step++)
        {
            int candidate = (selected + direction * step + resolutions.Length) % resolutions.Length;
            if (resolutions[candidate].width == currentWidth && resolutions[candidate].height == currentHeight)
                continue;

            session.Draft.resolutionWidth = resolutions[candidate].width;
            session.Draft.resolutionHeight = resolutions[candidate].height;
            RefreshFromDraft();
            return;
        }
    }

    private void ToggleDisplayMode()
    {
        if (session == null)
            return;
        session.Draft.fullScreenMode = session.Draft.fullScreenMode switch
        {
            (int)FullScreenMode.Windowed => (int)FullScreenMode.FullScreenWindow,
            (int)FullScreenMode.FullScreenWindow => (int)FullScreenMode.ExclusiveFullScreen,
            _ => (int)FullScreenMode.Windowed
        };
        RefreshFromDraft();
    }

    private void ChangeQuality(int direction)
    {
        if (session == null)
            return;
        int count = Mathf.Max(1, QualitySettings.names.Length);
        session.Draft.qualityLevel = (session.Draft.qualityLevel + direction + count) % count;
        RefreshFromDraft();
    }

    private void ToggleVSync()
    {
        if (session == null)
            return;
        session.Draft.verticalSync = !session.Draft.verticalSync;
        RefreshFromDraft();
    }

    private void SetFrameRate(int optionIndex)
    {
        if (session == null || optionIndex < 0 || optionIndex >= GameSettingsData.FrameRateLimits.Length)
            return;

        session.Draft.frameRateLimit = GameSettingsData.FrameRateLimits[optionIndex];
    }

    private void ToggleSmoothing()
    {
        if (session == null)
            return;
        session.Draft.cameraSmoothing = !session.Draft.cameraSmoothing;
        RefreshFromDraft();
    }

    private void ToggleInvert()
    {
        if (session == null)
            return;
        session.Draft.invertVerticalLook = !session.Draft.invertVerticalLook;
        RefreshFromDraft();
    }

    private void RefreshFromDraft()
    {
        if (session == null)
            return;
        GameSettingsData draft = session.Draft;
        masterSlider?.SetValueWithoutNotify(draft.masterVolume);
        musicSlider?.SetValueWithoutNotify(draft.musicVolume);
        effectsSlider?.SetValueWithoutNotify(draft.effectsVolume);
        interfaceSlider?.SetValueWithoutNotify(draft.interfaceVolume);
        interfaceOpacitySlider?.SetValueWithoutNotify(draft.interfaceOpacity);
        crosshairSizeSlider?.SetValueWithoutNotify(draft.crosshairSize);
        sensitivitySlider?.SetValueWithoutNotify(draft.mouseSensitivity);
        frameRateDropdown?.SetValueWithoutNotify(
            Mathf.Max(0, Array.IndexOf(GameSettingsData.FrameRateLimits, draft.frameRateLimit)));
        fovSlider?.SetValueWithoutNotify(draft.fieldOfView);
        smoothingIntensitySlider?.SetValueWithoutNotify(draft.cameraSmoothingIntensity);

        if (resolutionText != null) resolutionText.text = $"{draft.resolutionWidth} × {draft.resolutionHeight}";
        if (displayModeText != null)
        {
            displayModeText.text = draft.fullScreenMode switch
            {
                (int)FullScreenMode.Windowed => "Windowed",
                (int)FullScreenMode.FullScreenWindow => "Borderless",
                (int)FullScreenMode.ExclusiveFullScreen => "Fullscreen",
                _ => "Windowed"
            };
        }
        if (qualityText != null)
        {
            string[] names = QualitySettings.names;
            qualityText.text = names.Length == 0 ? "No levels" : names[Mathf.Clamp(draft.qualityLevel, 0, names.Length - 1)];
        }
        if (vsyncText != null) vsyncText.text = draft.verticalSync ? "On" : "Off";
        if (smoothingText != null) smoothingText.text = draft.cameraSmoothing ? "On" : "Off";
        if (invertText != null) invertText.text = draft.invertVerticalLook ? "On" : "Off";

        // SetValueWithoutNotify выше не поднимает onValueChanged, поэтому подписи обновляем вручную.
        UpdateSliderValues();
    }

    private void SelectTab(string id)
    {
        selectedTab = id;
        for (int i = 0; i < tabPages.Length; i++)
            tabPages[i].SetActive(i < tabIds.Length && tabIds[i] == id);
        for (int i = 0; i < tabButtons.Length; i++)
            tabButtons[i].interactable = i >= tabIds.Length || tabIds[i] != id;
    }

    private void AttachListeners()
    {
        closeButton?.onClick.AddListener(Close);
        applyButton?.onClick.AddListener(Apply);
        defaultsButton?.onClick.AddListener(AskForDefaults);
        confirmButton?.onClick.AddListener(ConfirmDialog);
        cancelDialogButton?.onClick.AddListener(CancelDialog);
        for (int i = 0; i < tabButtons.Length; i++)
        {
            int index = i;
            tabButtons[i]?.onClick.AddListener(() => SelectTab(index < tabIds.Length ? tabIds[index] : selectedTab));
        }
        previousResolutionButton?.onClick.AddListener(() => ChangeResolution(-1));
        nextResolutionButton?.onClick.AddListener(() => ChangeResolution(1));
        displayModeButton?.onClick.AddListener(ToggleDisplayMode);
        previousQualityButton?.onClick.AddListener(() => ChangeQuality(-1));
        nextQualityButton?.onClick.AddListener(() => ChangeQuality(1));
        vsyncButton?.onClick.AddListener(ToggleVSync);
        smoothingButton?.onClick.AddListener(ToggleSmoothing);
        invertButton?.onClick.AddListener(ToggleInvert);
        masterSlider?.onValueChanged.AddListener(value => service?.SetMasterVolume(value));
        musicSlider?.onValueChanged.AddListener(value => service?.SetMusicVolume(value));
        effectsSlider?.onValueChanged.AddListener(value => service?.SetEffectsVolume(value));
        interfaceSlider?.onValueChanged.AddListener(value => service?.SetInterfaceVolume(value));
        interfaceOpacitySlider?.onValueChanged.AddListener(value => service?.SetInterfaceOpacity(value));
        crosshairSizeSlider?.onValueChanged.AddListener(value => service?.SetCrosshairSize(value));
        sensitivitySlider?.onValueChanged.AddListener(value => service?.SetMouseSensitivity(value));
        fovSlider?.onValueChanged.AddListener(value => service?.SetFieldOfView(value));
        frameRateDropdown?.onValueChanged.AddListener(SetFrameRate);
        for (int i = 0; i < sliderValues.Count; i++)
            sliderValues[i].slider.onValueChanged.AddListener(_ => UpdateSliderValues());
        smoothingIntensitySlider?.onValueChanged.AddListener(value => { if (session != null) session.Draft.cameraSmoothingIntensity = value; });
    }

    private void DetachListeners()
    {
        closeButton?.onClick.RemoveListener(Close);
        applyButton?.onClick.RemoveListener(Apply);
        defaultsButton?.onClick.RemoveListener(AskForDefaults);
        confirmButton?.onClick.RemoveListener(ConfirmDialog);
        cancelDialogButton?.onClick.RemoveListener(CancelDialog);
        // Prefab is scene-owned; removing all listeners also clears the lambdas above.
        ClearButtonListeners(tabButtons);
        ClearButtonListeners(new[] { previousResolutionButton, nextResolutionButton, displayModeButton, previousQualityButton, nextQualityButton, vsyncButton, smoothingButton, invertButton });
        ClearSliderListeners(new[] { masterSlider, musicSlider, effectsSlider, interfaceSlider, interfaceOpacitySlider, crosshairSizeSlider, sensitivitySlider, fovSlider, smoothingIntensitySlider });
        frameRateDropdown?.onValueChanged.RemoveAllListeners();
    }

    private static void ClearButtonListeners(Button[] buttons)
    {
        for (int i = 0; i < buttons.Length; i++) buttons[i]?.onClick.RemoveAllListeners();
    }

    private static void ClearSliderListeners(Slider[] sliders)
    {
        for (int i = 0; i < sliders.Length; i++) sliders[i]?.onValueChanged.RemoveAllListeners();
    }

    private void CancelEdit()
    {
        session?.Cancel();
        session = null;
    }

#if UNITY_EDITOR
    public void EditorAssignReferences(
        GameObject overlay,
        GameObject confirmationPanel,
        TMP_Text confirmationText,
        Button closeButton,
        Button applyButton,
        Button defaultsButton,
        Button confirmButton,
        Button cancelDialogButton,
        Button[] tabButtons,
        GameObject[] tabPages,
        string[] tabIds,
        Button previousResolutionButton,
        Button nextResolutionButton,
        TMP_Text resolutionText,
        Button displayModeButton,
        TMP_Text displayModeText,
        Button previousQualityButton,
        Button nextQualityButton,
        TMP_Text qualityText,
        Button vsyncButton,
        TMP_Text vsyncText,
        TMP_Dropdown frameRateDropdown,
        Slider fovSlider,
        Button smoothingButton,
        TMP_Text smoothingText,
        Slider smoothingIntensitySlider,
        Slider masterSlider,
        Slider musicSlider,
        Slider effectsSlider,
        Slider sensitivitySlider,
        Button invertButton,
        TMP_Text invertText,
        Slider interfaceSlider,
        Slider interfaceOpacitySlider,
        Slider crosshairSizeSlider)
    {
        this.overlay = overlay;
        this.confirmationPanel = confirmationPanel;
        this.confirmationText = confirmationText;
        this.closeButton = closeButton;
        this.applyButton = applyButton;
        this.defaultsButton = defaultsButton;
        this.confirmButton = confirmButton;
        this.cancelDialogButton = cancelDialogButton;
        this.tabButtons = tabButtons;
        this.tabPages = tabPages;
        this.tabIds = tabIds;
        this.previousResolutionButton = previousResolutionButton;
        this.nextResolutionButton = nextResolutionButton;
        this.resolutionText = resolutionText;
        this.displayModeButton = displayModeButton;
        this.displayModeText = displayModeText;
        this.previousQualityButton = previousQualityButton;
        this.nextQualityButton = nextQualityButton;
        this.qualityText = qualityText;
        this.vsyncButton = vsyncButton;
        this.vsyncText = vsyncText;
        this.frameRateDropdown = frameRateDropdown;
        this.fovSlider = fovSlider;
        this.smoothingButton = smoothingButton;
        this.smoothingText = smoothingText;
        this.smoothingIntensitySlider = smoothingIntensitySlider;
        this.masterSlider = masterSlider;
        this.musicSlider = musicSlider;
        this.effectsSlider = effectsSlider;
        this.sensitivitySlider = sensitivitySlider;
        this.invertButton = invertButton;
        this.invertText = invertText;
        this.interfaceSlider = interfaceSlider;
        this.interfaceOpacitySlider = interfaceOpacitySlider;
        this.crosshairSizeSlider = crosshairSizeSlider;
    }
#endif
}
