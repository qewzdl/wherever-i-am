using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SettingsService : MonoBehaviour, ISettingsService
{
    private const float SaveDelaySeconds = 0.3f;
    private const float DisplayConfirmationSeconds = 15f;

    private static SettingsService instance;

    [Header("Persistence")]
    [SerializeField, Min(0.05f)] private float saveDelaySeconds = SaveDelaySeconds;
    [SerializeField, Min(1f)] private float displayConfirmationSeconds = DisplayConfirmationSeconds;

    private GameSettingsStorage storage;
    private GameSettingsData current;
    private GameSettingsData committed;
    private GameSettingsData displayFallback;
    private SettingsEditSession activeSession;
    private bool initialized;
    private bool savePending;
    private float saveAtTime;
    private bool displayConfirmationPending;
    private float displayConfirmationRemaining;
    private int revision;
    private float musicGain;

    public static SettingsService Instance => instance;
    public static bool TryGet(out ISettingsService service)
    {
        service = instance != null && instance.initialized ? instance : null;
        return service != null;
    }

    public GameSettingsData Current => current;
    public int Revision => revision;
    public bool IsDisplayConfirmationPending => displayConfirmationPending;
    public float DisplayConfirmationRemaining => displayConfirmationRemaining;

    public event Action<float> MusicGainChanged;
    public event Action<float> FovChanged;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogError($"Only one {nameof(SettingsService)} is allowed.", this);
            enabled = false;
            return;
        }

        instance = this;
    }

    private void Update()
    {
        if (!initialized)
            return;

        UpdateDisplayConfirmation();

        if (savePending && Time.unscaledTime >= saveAtTime)
            SaveImmediately();
    }

    private void OnApplicationQuit()
    {
        SaveImmediately();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    public bool Initialize()
    {
        if (initialized)
            return true;

        storage = new GameSettingsStorage();
        int qualityCount = QualitySettings.names.Length;
        GameSettingsData defaults = CreateDefaults();
        committed = storage.Load(defaults, qualityCount);
        current = committed.Clone();
        current.Sanitize(qualityCount);
        committed.Sanitize(qualityCount);
        initialized = true;

        ApplyNonDisplaySettings(current);
        ApplyDisplaySettings(current);
        RefreshMusicGain(true);
        revision++;
        return true;
    }

    public ISettingsEditSession BeginEdit()
    {
        EnsureInitialized();

        if (activeSession != null && !activeSession.IsCompleted)
            throw new InvalidOperationException("Only one settings edit session can be active.");

        activeSession = new SettingsEditSession(this, current.Clone());
        return activeSession;
    }

    public void SetMasterVolume(float value)
    {
        SetImmediateValue(value, (settings, clamped) => settings.masterVolume = clamped);
    }

    public void SetMusicVolume(float value)
    {
        SetImmediateValue(value, (settings, clamped) => settings.musicVolume = clamped);
    }

    public void SetEffectsVolume(float value)
    {
        SetImmediateValue(value, (settings, clamped) => settings.effectsVolume = clamped);
    }

    public void SetInterfaceVolume(float value)
    {
        SetImmediateValue(value, (settings, clamped) => settings.interfaceVolume = clamped);
    }

    public void SetInterfaceOpacity(float value)
    {
        SetImmediateValue(value, (settings, clamped) => settings.interfaceOpacity = clamped);
    }

    public void SetCrosshairSize(float value)
    {
        EnsureInitialized();
        float clamped = Mathf.Clamp(value, 0.5f, 2f);
        if (Mathf.Approximately(current.crosshairSize, clamped))
            return;

        current.crosshairSize = clamped;
        committed.crosshairSize = clamped;
        activeSession?.SetImmediateInterface(current);
        MarkChanged();
        ScheduleSave();
    }

    public void SetMouseSensitivity(float value)
    {
        EnsureInitialized();
        float clamped = Mathf.Clamp(value, 10f, 300f);

        if (Mathf.Approximately(current.mouseSensitivity, clamped))
            return;

        current.mouseSensitivity = clamped;
        committed.mouseSensitivity = clamped;
        activeSession?.SetImmediateMouseSensitivity(clamped);
        MarkChanged();
        ScheduleSave();
    }

    public void SetFieldOfView(float value)
    {
        EnsureInitialized();
        float clamped = Mathf.Clamp(value, 50f, 110f);
        if (Mathf.Approximately(current.fieldOfView, clamped))
            return;

        current.fieldOfView = clamped;
        committed.fieldOfView = clamped;
        if (activeSession != null)
            activeSession.Draft.fieldOfView = clamped;
        FovChanged?.Invoke(clamped);
        MarkChanged();
        ScheduleSave();
    }

    public void SetDebugSectionVisible(string sectionId, bool visible)
    {
        EnsureInitialized();

        switch (sectionId)
        {
            case "performance": SetDebugValue(ref current.debugShowPerformance, ref committed.debugShowPerformance, visible); break;
            case "player": SetDebugValue(ref current.debugShowPlayer, ref committed.debugShowPlayer, visible); break;
            case "network": SetDebugValue(ref current.debugShowNetwork, ref committed.debugShowNetwork, visible); break;
            case "enemies": SetDebugValue(ref current.debugShowEnemies, ref committed.debugShowEnemies, visible); break;
            case "scene": SetDebugValue(ref current.debugShowScene, ref committed.debugShowScene, visible); break;
            default: throw new ArgumentOutOfRangeException(nameof(sectionId), sectionId, "Unknown debug section.");
        }
    }

    public void SetDebugNoClipSpeed(float value)
    {
        EnsureInitialized();
        float clamped = Mathf.Clamp(value, 2f, 30f);

        if (Mathf.Approximately(current.debugNoClipSpeed, clamped))
            return;

        current.debugNoClipSpeed = clamped;
        committed.debugNoClipSpeed = clamped;
        MarkChanged();
        ScheduleSave();
    }

    public void Flush()
    {
        SaveImmediately();
    }

#if UNITY_EDITOR
    /// <summary>Точка входа для EditMode-тестов: не трогает пользовательский файл и экран.</summary>
    public void InitializeForTests(string filePath, GameSettingsData defaults, int qualityLevelCount)
    {
        if (initialized)
            throw new InvalidOperationException("Settings service is already initialized.");

        storage = new GameSettingsStorage(filePath);
        committed = storage.Load(defaults.Clone(), qualityLevelCount);
        current = committed.Clone();
        current.Sanitize(qualityLevelCount);
        committed.Sanitize(qualityLevelCount);
        initialized = true;
        RefreshMusicGain(true);
        revision++;
    }
#endif

    public void ConfirmDisplayChanges()
    {
        if (!displayConfirmationPending)
            return;

        committed.CopyDisplaySettingsFrom(current);
        displayFallback = null;
        displayConfirmationPending = false;
        displayConfirmationRemaining = 0f;
        SaveImmediately();
    }

    public void RevertDisplayChanges()
    {
        if (!displayConfirmationPending || displayFallback == null)
            return;

        current.CopyDisplaySettingsFrom(displayFallback);
        ApplyDisplaySettings(current);
        displayFallback = null;
        displayConfirmationPending = false;
        displayConfirmationRemaining = 0f;
        MarkChanged();
    }

    private void ApplySession(SettingsEditSession session)
    {
        if (!ReferenceEquals(activeSession, session))
            throw new InvalidOperationException("The settings edit session is no longer active.");

        GameSettingsData draft = session.Draft;
        draft.Sanitize(QualitySettings.names.Length);

        bool displayChanged = !committed.HasSameDisplaySettings(draft);
        GameSettingsData oldDisplay = committed.Clone();

        // Immediate values are already committed while the window is open.
        draft.masterVolume = committed.masterVolume;
        draft.musicVolume = committed.musicVolume;
        draft.effectsVolume = committed.effectsVolume;
        draft.interfaceVolume = committed.interfaceVolume;
        draft.mouseSensitivity = committed.mouseSensitivity;
        draft.debugShowPerformance = committed.debugShowPerformance;
        draft.debugShowPlayer = committed.debugShowPlayer;
        draft.debugShowNetwork = committed.debugShowNetwork;
        draft.debugShowEnemies = committed.debugShowEnemies;
        draft.debugShowScene = committed.debugShowScene;
        draft.debugNoClipSpeed = committed.debugNoClipSpeed;

        current.CopyFrom(draft);
        committed.CopyFrom(draft);

        if (displayChanged)
            committed.CopyDisplaySettingsFrom(oldDisplay);

        ApplyNonDisplaySettings(current);
        MarkChanged();

        if (displayChanged)
        {
            displayFallback = oldDisplay;
            displayConfirmationPending = true;
            displayConfirmationRemaining = displayConfirmationSeconds;
            ApplyDisplaySettings(current);
            return;
        }

        SaveImmediately();
    }

    private void CancelSession(SettingsEditSession session)
    {
        if (ReferenceEquals(activeSession, session))
            activeSession = null;
    }

    private void CompleteSession(SettingsEditSession session)
    {
        if (ReferenceEquals(activeSession, session))
            activeSession = null;
    }

    private void ResetSession(SettingsEditSession session)
    {
        if (!ReferenceEquals(activeSession, session))
            throw new InvalidOperationException("The settings edit session is no longer active.");

        GameSettingsData defaults = CreateDefaults();
        session.Draft.CopyFrom(defaults);

        // Sound and sensitivity follow the immediate rule even when reset.
        SetMasterVolume(defaults.masterVolume);
        SetMusicVolume(defaults.musicVolume);
        SetEffectsVolume(defaults.effectsVolume);
        SetInterfaceVolume(defaults.interfaceVolume);
        SetInterfaceOpacity(defaults.interfaceOpacity);
        SetCrosshairSize(defaults.crosshairSize);
        SetMouseSensitivity(defaults.mouseSensitivity);
        SetDebugNoClipSpeed(defaults.debugNoClipSpeed);
        SetDebugSectionVisible("performance", defaults.debugShowPerformance);
        SetDebugSectionVisible("player", defaults.debugShowPlayer);
        SetDebugSectionVisible("network", defaults.debugShowNetwork);
        SetDebugSectionVisible("enemies", defaults.debugShowEnemies);
        SetDebugSectionVisible("scene", defaults.debugShowScene);
    }

    private void SetImmediateValue(float value, Action<GameSettingsData, float> setValue)
    {
        EnsureInitialized();
        float clamped = Mathf.Clamp01(value);
        float previousGain = GetMusicGain(current);

        setValue.Invoke(current, clamped);
        setValue.Invoke(committed, clamped);
        activeSession?.ApplyImmediateAudio(current);
        RefreshMusicGain(previousGain, false);
        MarkChanged();
        ScheduleSave();
    }

    private void SetDebugValue(ref bool currentValue, ref bool committedValue, bool value)
    {
        if (currentValue == value)
            return;

        currentValue = value;
        committedValue = value;
        MarkChanged();
        ScheduleSave();
    }

    private void UpdateDisplayConfirmation()
    {
        if (!displayConfirmationPending)
            return;

        displayConfirmationRemaining -= Time.unscaledDeltaTime;

        if (displayConfirmationRemaining <= 0f)
            RevertDisplayChanges();
    }

    private void ApplyNonDisplaySettings(GameSettingsData settings)
    {
        QualitySettings.SetQualityLevel(settings.qualityLevel, true);
        QualitySettings.vSyncCount = settings.verticalSync ? 1 : 0;
        Application.targetFrameRate = settings.frameRateLimit;
    }

    private static void ApplyDisplaySettings(GameSettingsData settings)
    {
        Screen.SetResolution(
            settings.resolutionWidth,
            settings.resolutionHeight,
            (FullScreenMode)settings.fullScreenMode);
    }

    private void RefreshMusicGain(bool force)
    {
        RefreshMusicGain(GetMusicGain(current), force);
    }

    private void RefreshMusicGain(float previousGain, bool force)
    {
        float nextGain = GetMusicGain(current);

        if (!force && Mathf.Approximately(previousGain, nextGain))
            return;

        musicGain = nextGain;
        MusicGainChanged?.Invoke(nextGain);
    }

    private static float GetMusicGain(GameSettingsData settings)
    {
        return settings != null
            ? Mathf.Clamp01(settings.masterVolume) * Mathf.Clamp01(settings.musicVolume)
            : 1f;
    }

    private void MarkChanged()
    {
        revision++;
    }

    private void ScheduleSave()
    {
        savePending = true;
        saveAtTime = Time.unscaledTime + saveDelaySeconds;
    }

    private void SaveImmediately()
    {
        if (!initialized || storage == null || committed == null)
            return;

        storage.Save(committed, QualitySettings.names.Length);
        savePending = false;
    }

    private GameSettingsData CreateDefaults()
    {
        Resolution resolution = Screen.currentResolution;
        return GameSettingsData.CreateDefaults(
            resolution.width > 0 ? resolution.width : 1920,
            resolution.height > 0 ? resolution.height : 1080,
            QualitySettings.GetQualityLevel());
    }

    private void EnsureInitialized()
    {
        if (!initialized && !Initialize())
            throw new InvalidOperationException($"{nameof(SettingsService)} is not initialized.");
    }

    private sealed class SettingsEditSession : ISettingsEditSession
    {
        private SettingsService owner;

        internal SettingsEditSession(SettingsService owner, GameSettingsData draft)
        {
            this.owner = owner;
            Draft = draft;
        }

        public GameSettingsData Draft { get; }
        public bool IsCompleted => owner == null;

        public void ResetToDefaults()
        {
            RequireOwner().ResetSession(this);
        }

        public void Apply()
        {
            SettingsService service = RequireOwner();
            service.ApplySession(this);
            service.CompleteSession(this);
            owner = null;
        }

        public void Cancel()
        {
            SettingsService service = owner;
            if (service == null)
                return;

            service.CancelSession(this);
            owner = null;
        }

        public void Dispose()
        {
            Cancel();
        }

        internal void ApplyImmediateAudio(GameSettingsData source)
        {
            Draft.masterVolume = source.masterVolume;
            Draft.musicVolume = source.musicVolume;
            Draft.effectsVolume = source.effectsVolume;
            Draft.interfaceVolume = source.interfaceVolume;
            Draft.interfaceOpacity = source.interfaceOpacity;
            Draft.crosshairSize = source.crosshairSize;
        }

        internal void SetImmediateInterface(GameSettingsData source)
        {
            Draft.interfaceOpacity = source.interfaceOpacity;
            Draft.crosshairSize = source.crosshairSize;
        }

        internal void SetImmediateMouseSensitivity(float value)
        {
            Draft.mouseSensitivity = value;
        }

        private SettingsService RequireOwner()
        {
            if (owner == null)
                throw new ObjectDisposedException(nameof(SettingsEditSession));

            return owner;
        }
    }
}
