using UnityEngine;

// Hands the settings service to the scene UI that wants it. The consumers are
// listed here rather than searched for: they live in another assembly, cannot
// ask for the service themselves, and a scene-wide sweep for whoever happens to
// implement an interface hides that dependency instead of stating it.
public sealed class SettingsConsumersSceneFeature : SceneRuntimeFeature
{
    [SerializeField] private MonoBehaviour[] consumers;

    protected override bool ValidateFeature(SceneFeatureContext context)
    {
        bool valid = RequireService<ISettingsService>(context, out _);

        if (consumers == null || consumers.Length == 0)
        {
            Debug.LogError(
                $"{nameof(SettingsConsumersSceneFeature)} has no consumers listed.",
                this);
            return false;
        }

        for (int i = 0; i < consumers.Length; i++)
        {
            if (consumers[i] is ISettingsServiceConsumer)
                continue;

            Debug.LogError(
                $"{nameof(SettingsConsumersSceneFeature)} entry {i} " +
                $"('{(consumers[i] == null ? "missing" : consumers[i].GetType().Name)}') " +
                $"is not an {nameof(ISettingsServiceConsumer)}.",
                this);
            valid = false;
        }

        return valid;
    }

    protected override bool InstallFeature(SceneFeatureContext context)
    {
        ISettingsService settingsService = context.Services.Resolve<ISettingsService>();

        for (int i = 0; i < consumers.Length; i++)
        {
            if (consumers[i] is ISettingsServiceConsumer consumer)
                consumer.Construct(settingsService);
        }

        return true;
    }

    protected override void UninstallFeature(SceneFeatureContext context)
    {
        if (consumers == null)
            return;

        for (int i = 0; i < consumers.Length; i++)
        {
            if (consumers[i] is ISettingsServiceConsumer consumer)
                RunCleanup(() => consumer.ReleaseSettingsService(), consumers[i]);
        }
    }
}
