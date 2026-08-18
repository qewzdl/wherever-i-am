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
    void SetCameraSmoothing(bool value);
    void SetCameraSmoothingIntensity(float value);
    void SetInvertVerticalLook(bool value);

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
