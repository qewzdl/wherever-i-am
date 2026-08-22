using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// The settings screen, in UI Toolkit.
//
// The logic underneath is untouched: ISettingsService still owns the values and
// still hands out an edit session, so nothing is applied until Apply and a
// display change still has to be confirmed before it sticks. What changed is
// the controls - lists instead of arrow pairs, toggles instead of buttons that
// spell out their own state - and where the looks come from.
[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class SettingsDocument : MonoBehaviour, ISettingsServiceConsumer, ISettingsScreen
{
    private const string OpenClass = "screen--open";
    private const string ActiveTabClass = "tab--active";
    private const string OverlayOpenClass = "overlay--open";
    private const string ConfirmToneClass = "button--confirm";
    private const string DangerToneClass = "button--danger";
    // Longer than --motion-screen (0.26s), which is what the screen's opacity
    // actually takes. Anything shorter switches display off mid-fade and the
    // screen vanishes at half brightness - the one animation in the interface
    // nobody could work out why it looked broken.
    private const long FadeMilliseconds = 300;

    [Header("References")]
    [SerializeField] private UIDocument document;
    [SerializeField] private UiDocumentSounds sounds;

    private ISettingsService settingsService;
    private ISettingsEditSession session;

    // The tree this was bound to. Binding subscribes, so doing it twice would
    // make every slider write twice and every tab click count twice; and a
    // document that is switched off rebuilds its tree, which makes the old
    // references stale rather than merely duplicated.
    private VisualElement boundRoot;
    private VisualElement screen;
    private VisualElement confirmPanel;
    private Label confirmText;
    private Button confirmButton;
    private Button revertButton;
    private IVisualElementScheduledItem hideAfterFade;

    private readonly Dictionary<string, VisualElement> pages = new();
    private readonly Dictionary<string, Button> tabs = new();
    private readonly List<Resolution> resolutions = new();
    private enum PendingQuestion
    {
        None,
        Defaults,
        DiscardChanges,
        DisplayConfirmation
    }

    private string selectedTab = "Graphics";
    private PendingQuestion question;
    private Button applyButton;
    private bool isOpen;

    // One screen for the whole game, so it outlives the scene it was loaded
    // with - the same arrangement the audio and error UI already have.
    private void Awake()
    {
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        if (document == null)
            document = GetComponent<UIDocument>();

        if (sounds == null)
            sounds = GetComponent<UiDocumentSounds>();
    }

    // A screen covers everything and takes the pointer with it, so it has to be
    // harmless before anybody constructs it. Nothing guarantees that a consumer
    // is ever handed a service - and an invisible screen eating every click is
    // the kind of fault that looks like a broken button somewhere else.
    private void OnEnable()
    {
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        HideUntilOpened();
    }

    // UIDocument builds its tree in its own OnEnable, and component order on one
    // object is not something to rely on.
    private void Start()
    {
        HideUntilOpened();
    }

    private void HideUntilOpened()
    {
        if (isOpen || !Bind())
            return;

        SetScreenVisible(false);
        HideConfirmation();
    }

    public void Construct(ISettingsService settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        ReleaseSettingsService();
        settingsService = settings;
        HideUntilOpened();
    }

    public void ReleaseSettingsService()
    {
        CancelSession();
        settingsService = null;
    }

    private void OnDestroy()
    {
        screen?.UnregisterCallback<NavigationCancelEvent>(HandleCancelPressed);
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        ReleaseSettingsService();
    }

    // Answered the way the right button answers it: leave things alone. On the
    // display countdown that means Revert, which is the whole point of the
    // question - somebody looking at a screen mode they cannot read reaches for
    // Escape long before they find a button.
    private void HandleCancelPressed(NavigationCancelEvent evt)
    {
        if (!isOpen)
            return;

        // The pause menu reads the escape key straight off the keyboard rather
        // than waiting to hear whether a window took it, so a screen that opens
        // from the pause menu has to say it did. The chat window already had to
        // do this; it is the mechanism that exists.
        PauseMenuInput.SuppressToggleForCurrentFrame();

        if (question != PendingQuestion.None)
        {
            RevertPending();
            evt.StopPropagation();
            return;
        }

        Close();
        evt.StopPropagation();
    }

    public bool IsOpen => isOpen;

    // Surviving the scene means surviving whatever was behind it. A screen left
    // standing over the main menu after a match, still editing settings through
    // a session nobody is in, is worse than one that shuts when the ground
    // moves - so it shuts, without asking, because there is nobody left to ask.
    private void HandleActiveSceneChanged(Scene from, Scene to)
    {
        if (isOpen)
            CloseNow();
    }

    public void Open()
    {
        if (settingsService == null || screen == null || isOpen)
            return;

        // A service allows one edit session at a time and throws on a second,
        // so an old one is ended before asking for another.
        CancelSession();
        session = settingsService.BeginEdit();
        question = PendingQuestion.None;
        RefreshFromDraft();
        SelectTab(selectedTab);
        HideConfirmation();
        SetScreenVisible(true);
        sounds?.Play(UiSoundType.Open);
    }

    // Closes whatever state it is in. A display change that has not been
    // confirmed is not a reason to hold the screen open: the service reverts it
    // by itself when the countdown runs out, and a window that refuses to close
    // is a window standing over a game the player is trying to play.
    public void Close()
    {
        if (!isOpen)
            return;

        // Nothing here is applied until Apply, so closing silently would throw
        // away everything the player has just set. It is asked about instead.
        if (HasStagedChanges())
        {
            question = PendingQuestion.DiscardChanges;
            ShowConfirmation("Close without applying the changes?");
            return;
        }

        CloseNow();
    }

    private void CloseNow()
    {
        CancelSession();
        HideConfirmation();
        question = PendingQuestion.None;
        SetScreenVisible(false);
        sounds?.Play(UiSoundType.Close);
    }

    // The countdown only matters while the screen is up; once it is closed the
    // service finishes the revert on its own.
    private void Update()
    {
        if (!isOpen || settingsService == null)
            return;

        if (settingsService.IsDisplayConfirmationPending)
        {
            // A question the player is already looking at is not talked over.
            // Applying a resolution and then editing something else leaves both
            // questions wanting the one panel, and the countdown does not need
            // an answer to end - it reverts by itself if nobody gets to it.
            if (question == PendingQuestion.None ||
                question == PendingQuestion.DisplayConfirmation)
            {
                question = PendingQuestion.DisplayConfirmation;
                ShowDisplayConfirmation();
            }

            return;
        }

        // The countdown ran out and the service reverted by itself. Nobody
        // pressed anything, so nobody takes the question down either - unless
        // it happens here. Without this the panel stands there reading
        // "Reverting in 1 s" over a screen that has already gone back.
        if (question == PendingQuestion.DisplayConfirmation)
            FinishDisplayConfirmation();
    }

    // However the display question ends - either button, or the countdown
    // running out - the draft is left describing a resolution that may no
    // longer be the one on screen, so it is thrown away and taken again.
    private void FinishDisplayConfirmation()
    {
        CancelSession();
        session = settingsService?.BeginEdit();
        RefreshFromDraft();
        question = PendingQuestion.None;
        HideConfirmation();
    }

    private bool Bind()
    {
        if (document == null)
            document = GetComponent<UIDocument>();

        VisualElement root = document != null ? document.rootVisualElement : null;

        if (root == null)
        {
            Debug.LogError($"{nameof(SettingsDocument)} has no document to bind.", this);
            return false;
        }

        if (ReferenceEquals(root, boundRoot))
            return screen != null;

        boundRoot = root;
        screen = root.Q<VisualElement>("Screen");
        confirmPanel = root.Q<VisualElement>("ConfirmPanel");
        confirmText = root.Q<Label>("ConfirmText");

        if (screen == null)
        {
            Debug.LogError($"{nameof(SettingsDocument)} did not find 'Screen'.", this);
            return false;
        }

        BindTabs(root);
        BindGraphics(root);
        BindAudio(root);
        BindControls(root);
        BindInterface(root);
        BindFooter(root);
        return true;
    }

    private void BindTabs(VisualElement root)
    {
        tabs.Clear();
        pages.Clear();

        AddTab(root, "Graphics");
        AddTab(root, "Audio");
        AddTab(root, "Controls");
        AddTab(root, "Interface");
    }

    private void AddTab(VisualElement root, string id)
    {
        Button tab = root.Q<Button>(id + "Tab");
        VisualElement page = root.Q<VisualElement>(id + "Page");

        if (tab == null || page == null)
            return;

        tabs[id] = tab;
        pages[id] = page;
        tab.clicked += () => SelectTab(id);
    }

    private void SelectTab(string id)
    {
        if (!pages.ContainsKey(id))
            return;

        selectedTab = id;

        foreach (KeyValuePair<string, VisualElement> page in pages)
        {
            bool active = page.Key == id;

            page.Value.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;

            if (!tabs.TryGetValue(page.Key, out Button tab))
                continue;

            if (active)
                tab.AddToClassList(ActiveTabClass);
            else
                tab.RemoveFromClassList(ActiveTabClass);
        }
    }

    private void BindGraphics(VisualElement root)
    {
        DropdownField resolution = root.Q<DropdownField>("Resolution");

        if (resolution != null)
        {
            FillResolutions(resolution);
            resolution.RegisterValueChangedCallback(evt =>
            {
                int index = resolution.choices.IndexOf(evt.newValue);

                if (session == null || index < 0 || index >= resolutions.Count)
                    return;

                session.Draft.resolutionWidth = resolutions[index].width;
                session.Draft.resolutionHeight = resolutions[index].height;
                RefreshApplyButton();
            });
        }

        DropdownField displayMode = root.Q<DropdownField>("DisplayMode");

        if (displayMode != null)
        {
            displayMode.choices = new List<string> { "Fullscreen", "Borderless", "Windowed" };
            displayMode.RegisterValueChangedCallback(evt =>
            {
                if (session == null)
                    return;

                session.Draft.fullScreenMode = (int)ToFullScreenMode(evt.newValue);
                RefreshApplyButton();
            });
        }

        DropdownField quality = root.Q<DropdownField>("Quality");

        if (quality != null)
        {
            quality.choices = new List<string>(QualitySettings.names);
            quality.RegisterValueChangedCallback(evt =>
            {
                int index = quality.choices.IndexOf(evt.newValue);

                if (session != null && index >= 0)
                    session.Draft.qualityLevel = index;

                RefreshApplyButton();
            });
        }

        DropdownField frameRate = root.Q<DropdownField>("FrameRate");

        if (frameRate != null)
        {
            frameRate.choices = BuildFrameRateChoices();
            frameRate.RegisterValueChangedCallback(evt =>
            {
                int index = frameRate.choices.IndexOf(evt.newValue);

                if (session != null && index >= 0)
                    session.Draft.frameRateLimit = GameSettingsData.FrameRateLimits[index];

                RefreshApplyButton();
            });
        }

        BindToggle(root, "VerticalSync", value => session.Draft.verticalSync = value);
        BindToggle(root, "CameraSmoothing", value => session.Draft.cameraSmoothing = value);

        BindSlider(root, "FieldOfView", value => session.Draft.fieldOfView = value, FormatWhole);
        BindSlider(
            root,
            "SmoothingIntensity",
            value => session.Draft.cameraSmoothingIntensity = value,
            FormatPercent);
    }

    private void BindAudio(VisualElement root)
    {
        BindSlider(root, "MasterVolume", value => session.Draft.masterVolume = value, FormatPercent);
        BindSlider(root, "MusicVolume", value => session.Draft.musicVolume = value, FormatPercent);
        BindSlider(root, "EffectsVolume", value => session.Draft.effectsVolume = value, FormatPercent);
        BindSlider(root, "InterfaceVolume", value => session.Draft.interfaceVolume = value, FormatPercent);
    }

    private void BindControls(VisualElement root)
    {
        BindSlider(root, "MouseSensitivity", value => session.Draft.mouseSensitivity = value, FormatWhole);
        BindToggle(root, "InvertVerticalLook", value => session.Draft.invertVerticalLook = value);
    }

    private void BindInterface(VisualElement root)
    {
        BindSlider(root, "InterfaceOpacity", value => session.Draft.interfaceOpacity = value, FormatPercent);
        BindSlider(root, "CrosshairSize", value => session.Draft.crosshairSize = value, FormatPercent);
    }

    private void BindFooter(VisualElement root)
    {
        Button apply = root.Q<Button>("ApplyButton");
        Button defaults = root.Q<Button>("DefaultsButton");
        Button close = root.Q<Button>("CloseButton");
        Button confirm = root.Q<Button>("ConfirmButton");
        Button revert = root.Q<Button>("RevertButton");

        applyButton = apply;

        if (apply != null)
            apply.clicked += Apply;

        if (defaults != null)
            defaults.clicked += AskForDefaults;

        if (close != null)
            close.clicked += Close;

        confirmButton = confirm;
        revertButton = revert;

        // Escape, and the cancel button on a pad. It backs out of the question
        // first and out of the screen second, which is the order they are
        // stacked in.
        screen?.RegisterCallback<NavigationCancelEvent>(HandleCancelPressed);

        if (confirm != null)
            confirm.clicked += ConfirmPending;

        if (revert != null)
            revert.clicked += RevertPending;
    }

    private void BindSlider(
        VisualElement root,
        string name,
        Action<float> write,
        Func<float, string> format)
    {
        Slider slider = root.Q<Slider>(name);
        Label value = root.Q<Label>(name + "Value");

        if (slider == null)
            return;

        slider.RegisterValueChangedCallback(evt =>
        {
            write(evt.newValue);

            if (value != null)
                value.text = format(evt.newValue);

            RefreshApplyButton();
        });
    }

    private void BindToggle(VisualElement root, string name, Action<bool> write)
    {
        Toggle toggle = root.Q<Toggle>(name);

        if (toggle == null)
            return;

        toggle.RegisterValueChangedCallback(evt =>
        {
            if (session != null)
                write(evt.newValue);

            RefreshApplyButton();
        });
    }

    private void RefreshFromDraft()
    {
        if (session == null || document == null)
            return;

        VisualElement root = document.rootVisualElement;
        GameSettingsData draft = session.Draft;

        SetDropdown(root, "Resolution", ResolutionLabel(draft.resolutionWidth, draft.resolutionHeight));
        SetDropdown(root, "DisplayMode", DisplayModeLabel((FullScreenMode)draft.fullScreenMode));
        SetDropdown(root, "Quality", IndexLabel(QualitySettings.names, draft.qualityLevel));
        SetDropdown(root, "FrameRate", FrameRateLabel(draft.frameRateLimit));

        SetToggle(root, "VerticalSync", draft.verticalSync);
        SetToggle(root, "CameraSmoothing", draft.cameraSmoothing);
        SetToggle(root, "InvertVerticalLook", draft.invertVerticalLook);

        SetSlider(root, "FieldOfView", draft.fieldOfView, FormatWhole);
        SetSlider(root, "SmoothingIntensity", draft.cameraSmoothingIntensity, FormatPercent);
        SetSlider(root, "MasterVolume", draft.masterVolume, FormatPercent);
        SetSlider(root, "MusicVolume", draft.musicVolume, FormatPercent);
        SetSlider(root, "EffectsVolume", draft.effectsVolume, FormatPercent);
        SetSlider(root, "InterfaceVolume", draft.interfaceVolume, FormatPercent);
        SetSlider(root, "MouseSensitivity", draft.mouseSensitivity, FormatWhole);
        SetSlider(root, "InterfaceOpacity", draft.interfaceOpacity, FormatPercent);
        SetSlider(root, "CrosshairSize", draft.crosshairSize, FormatPercent);
        RefreshApplyButton();
    }

    // Set without notifying: the value came from the draft, and letting it come
    // back as a change would write it straight back in again.
    private static void SetSlider(
        VisualElement root,
        string name,
        float value,
        Func<float, string> format)
    {
        Slider slider = root.Q<Slider>(name);
        Label label = root.Q<Label>(name + "Value");

        slider?.SetValueWithoutNotify(value);

        if (label != null)
            label.text = format(value);
    }

    private static void SetToggle(VisualElement root, string name, bool value)
    {
        root.Q<Toggle>(name)?.SetValueWithoutNotify(value);
    }

    private static void SetDropdown(VisualElement root, string name, string value)
    {
        DropdownField dropdown = root.Q<DropdownField>(name);

        if (dropdown != null && !string.IsNullOrEmpty(value))
            dropdown.SetValueWithoutNotify(value);
    }

    private void Apply()
    {
        if (session == null || session.IsCompleted)
            return;

        session.Apply();
        session = settingsService.BeginEdit();
        RefreshFromDraft();

        if (settingsService.IsDisplayConfirmationPending)
            ShowDisplayConfirmation();
    }

    // Nothing on this screen takes effect before Apply, so the question is the
    // simple one: does the draft still say what the game is already doing.
    //
    // Compared as text rather than field by field. A written-out list of
    // comparisons has to be extended every time a setting is added, and the day
    // somebody forgets is the day Apply sits grey over real changes - a failure
    // this screen has had once already. Settings are stored as JSON anyway, so
    // this is the same notion of equality that persistence uses.
    private bool HasStagedChanges()
    {
        if (session == null || session.IsCompleted || settingsService == null)
            return false;

        return JsonUtility.ToJson(session.Draft) != JsonUtility.ToJson(settingsService.Current);
    }

    // The button says whether there is anything to apply, which is also how the
    // player learns that this screen waits: it lights up the moment they touch
    // something, and goes out again when they put it back.
    private void RefreshApplyButton()
    {
        applyButton?.SetEnabled(HasStagedChanges());
    }

    private void AskForDefaults()
    {
        if (session == null)
            return;

        question = PendingQuestion.Defaults;
        ShowConfirmation("Reset every setting to its default?");
    }

    // The left button always means "go ahead with what was asked".
    private void ConfirmPending()
    {
        switch (question)
        {
            case PendingQuestion.Defaults:
                session?.ResetToDefaults();
                RefreshFromDraft();
                break;

            case PendingQuestion.DiscardChanges:
                HideConfirmation();
                question = PendingQuestion.None;
                CloseNow();
                return;

            case PendingQuestion.DisplayConfirmation:
                settingsService?.ConfirmDisplayChanges();
                FinishDisplayConfirmation();
                return;
        }

        question = PendingQuestion.None;
        HideConfirmation();
    }

    // And the right button always means "leave things as they were".
    private void RevertPending()
    {
        switch (question)
        {
            case PendingQuestion.DisplayConfirmation:
                settingsService?.RevertDisplayChanges();
                FinishDisplayConfirmation();
                return;
        }

        question = PendingQuestion.None;
        HideConfirmation();
    }

    private void ShowDisplayConfirmation()
    {
        int remaining = Mathf.CeilToInt(settingsService.DisplayConfirmationRemaining);
        ShowConfirmation($"Keep these display settings? Reverting in {remaining} s");
    }

    private void ShowConfirmation(string message)
    {
        if (confirmText != null)
            confirmText.text = message;

        SetAnswerLabels();

        UiFade.Set(confirmPanel, true, OverlayOpenClass);

        // The right button takes the focus, because it is the one that leaves
        // things alone. Two of the three questions put throwing work away on
        // the left, and a dialog that arrives with Enter aimed at that is worse
        // than no dialog.
        if (revertButton != null)
            revertButton.schedule.Execute(() => revertButton.Focus());
    }

    // The left button always goes ahead with what was asked and the right one
    // always leaves things alone. What changes with the question is not only
    // the words but which of the two is the one you probably want.
    //
    // The markup used to decide that: confirm was nailed to the left button, so
    // it wore the accent before anybody pointed at it - for Keep, which is what
    // the accent is for, and equally for Reset and Discard, which throw work
    // away. On the discard question it was pointing at the wrong answer
    // outright: the one to take is Keep editing, and that was the dim one.
    private void SetAnswerLabels()
    {
        switch (question)
        {
            case PendingQuestion.Defaults:
                SetAnswer("Reset", DangerToneClass, "Cancel", null);
                break;

            case PendingQuestion.DiscardChanges:
                SetAnswer("Discard", DangerToneClass, "Keep editing", ConfirmToneClass);
                break;

            case PendingQuestion.DisplayConfirmation:
                SetAnswer("Keep", ConfirmToneClass, "Revert", null);
                break;
        }
    }

    private void SetAnswer(
        string confirmLabel,
        string confirmTone,
        string revertLabel,
        string revertTone)
    {
        SetAnswer(confirmButton, confirmLabel, confirmTone);
        SetAnswer(revertButton, revertLabel, revertTone);
    }

    // Both tones come off before one goes on. The dialog is reused for every
    // question, so a button that was rust last time is still rust unless
    // somebody says otherwise.
    private static void SetAnswer(Button button, string label, string tone)
    {
        if (button == null)
            return;

        button.text = label;

        button.RemoveFromClassList(ConfirmToneClass);
        button.RemoveFromClassList(DangerToneClass);

        if (!string.IsNullOrEmpty(tone))
            button.AddToClassList(tone);
    }

    private void HideConfirmation()
    {
        UiFade.Set(confirmPanel, false, OverlayOpenClass);
    }

    private void CancelSession()
    {
        if (session != null && !session.IsCompleted)
            session.Cancel();

        session = null;
    }

    private void SetScreenVisible(bool visible)
    {
        if (screen == null)
            return;

        isOpen = visible;
        screen.pickingMode = visible ? PickingMode.Position : PickingMode.Ignore;
        hideAfterFade?.Pause();
        hideAfterFade = null;

        if (visible)
        {
            screen.style.display = DisplayStyle.Flex;
            screen.schedule.Execute(() => screen.AddToClassList(OpenClass));
            return;
        }

        screen.RemoveFromClassList(OpenClass);
        hideAfterFade = screen.schedule
            .Execute(() => screen.style.display = DisplayStyle.None)
            .StartingIn(FadeMilliseconds);
    }

    private void FillResolutions(DropdownField dropdown)
    {
        resolutions.Clear();
        List<string> choices = new();

        Resolution[] available = Screen.resolutions;

        for (int i = 0; i < available.Length; i++)
        {
            string label = ResolutionLabel(available[i].width, available[i].height);

            if (choices.Contains(label))
                continue;

            resolutions.Add(available[i]);
            choices.Add(label);
        }

        dropdown.choices = choices;
    }

    private static List<string> BuildFrameRateChoices()
    {
        List<string> choices = new();

        for (int i = 0; i < GameSettingsData.FrameRateLimits.Length; i++)
            choices.Add(FrameRateLabel(GameSettingsData.FrameRateLimits[i]));

        return choices;
    }

    private static string ResolutionLabel(int width, int height)
    {
        return $"{width} x {height}";
    }

    private static string FrameRateLabel(int limit)
    {
        return limit <= 0 ? "Unlimited" : $"{limit} fps";
    }

    private static string DisplayModeLabel(FullScreenMode mode)
    {
        return mode switch
        {
            FullScreenMode.ExclusiveFullScreen => "Fullscreen",
            FullScreenMode.FullScreenWindow => "Borderless",
            _ => "Windowed"
        };
    }

    private static FullScreenMode ToFullScreenMode(string label)
    {
        return label switch
        {
            "Fullscreen" => FullScreenMode.ExclusiveFullScreen,
            "Borderless" => FullScreenMode.FullScreenWindow,
            _ => FullScreenMode.Windowed
        };
    }

    private static string IndexLabel(string[] names, int index)
    {
        return names != null && index >= 0 && index < names.Length ? names[index] : string.Empty;
    }

    private static string FormatPercent(float value)
    {
        return $"{Mathf.RoundToInt(value * 100f)}%";
    }

    private static string FormatWhole(float value)
    {
        return Mathf.RoundToInt(value).ToString();
    }
}
