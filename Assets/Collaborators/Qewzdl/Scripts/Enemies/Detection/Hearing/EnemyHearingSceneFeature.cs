public sealed class EnemyHearingSceneFeature : SceneRuntimeFeature
{
    protected override bool ValidateFeature(SceneFeatureContext context)
    {
        if (!RequireService(context, out IGameplayNoiseService noiseService))
            return false;

        if (!noiseService.IsInitialized)
        {
            UnityEngine.Debug.LogError(
                $"{nameof(EnemyHearingSceneFeature)} requires initialized " +
                $"{nameof(IGameplayNoiseService)} from the Session scope.",
                this
            );

            return false;
        }

        return true;
    }

    protected override bool InstallFeature(SceneFeatureContext context)
    {
        return true;
    }
}
