public interface IUiSoundService
{
    void ApplyTheme(UiSoundTheme theme);
    void ClearTheme();
    void PlayClick();
    void PlayHover();
    void PlayOpen();
    void PlayClose();
    void PlayConfirm();
    void PlayCancel();
    void PlayError();
    void PlayInput();
    void Play(UiSoundType type);
    bool TryPlay(UiSoundType type);
    void Play(SoundEffect sound);
    bool TryPlay(SoundEffect sound);
    void SetMasterVolume(float volume);
}
