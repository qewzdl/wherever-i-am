public sealed class EnemyHearingSceneFeature : SceneRuntimeFeature
{
    protected override bool ValidateFeature(ProjectContext context)
    {
        GameplayNoiseWorldService noiseWorldService = context.GameplayNoiseWorld;

        if (!RequireService(noiseWorldService, nameof(context.GameplayNoiseWorld)))
            return false;

        if (!noiseWorldService.IsInitialized)
        {
            UnityEngine.Debug.LogError(
                $"{nameof(EnemyHearingSceneFeature)} requires initialized " +
                $"{nameof(GameplayNoiseWorldService)} from {nameof(ProjectContext)}.",
                this
            );

            return false;
        }

        return true;
    }

    protected override bool InstallFeature(ProjectContext context)
    {
        return true;
    }
}
