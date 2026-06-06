using UnityEngine;

[DefaultExecutionOrder(-950)]
[DisallowMultipleComponent]
public sealed class EnemyNoiseWorldServiceBootstrapInstaller : MonoBehaviour
{
    private bool installFailureLogged;

    private void Awake()
    {
        Install();
    }

    public bool Install()
    {
        ProjectContext context = ProjectContext.Instance;
        GameplayNoiseWorldService noiseWorldService = context != null
            ? context.GameplayNoiseWorld
            : null;

        if (context != null &&
            noiseWorldService != null &&
            noiseWorldService.Construct(context.NetworkManager))
        {
            installFailureLogged = false;
            return true;
        }

        if (!installFailureLogged)
        {
            installFailureLogged = true;

            Debug.LogError(
                $"{nameof(EnemyNoiseWorldServiceBootstrapInstaller)} requires initialized " +
                $"{nameof(GameplayNoiseWorldService)} from {nameof(ProjectContext)}.",
                this
            );
        }

        return false;
    }
}
