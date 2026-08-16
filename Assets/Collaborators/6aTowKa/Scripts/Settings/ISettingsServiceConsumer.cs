public interface ISettingsServiceConsumer
{
    void Construct(ISettingsService settingsService);
    void ReleaseSettingsService();
}
