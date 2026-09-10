using System;

public interface ISettingsService
{
    GameSettingsData Current { get; }
    int Revision { get; }
    bool IsDisplayConfirmationPending { get; }
    float DisplayConfirmationRemaining { get; }

    event Action<float> MusicGainChanged;
    event Action<float> FovChanged;
    event Action SettingsChanged;

    ISettingsEditSession BeginEdit();

    void SetMasterVolume(float value);
    void SetMusicVolume(float value);
    void SetEffectsVolume(float value);
    void SetInterfaceVolume(float value);
    void SetInterfaceOpacity(float value);
    void SetCrosshairSize(float value);
    void SetMouseSensitivity(float value);
    void SetFieldOfView(float value);
    void SetCameraSmoothingIntensity(float value);
    void SetInvertVerticalLook(bool value);

    // Per-effect strength for the cosmetic camera effects, 0..1. There is no matching
    // on/off setter: 0 is off, and a separate toggle would only give the settings screen
    // two controls that contradict each other.
    void SetCameraShakeIntensity(float value);
    void SetHeadBobIntensity(float value);
    void SetCameraRollIntensity(float value);
    void SetStrafeLeanIntensity(float value);
    void SetBreathingIntensity(float value);

    void SetDebugSectionVisible(string sectionId, bool visible);
    void SetDebugNoClipSpeed(float value);
    void Flush();

    void ConfirmDisplayChanges();
    void RevertDisplayChanges();
}

public interface ISettingsEditSession : IDisposable
{
    GameSettingsData Draft { get; }
    bool IsCompleted { get; }

    void ResetToDefaults();
    void Apply();
    void Cancel();
}
