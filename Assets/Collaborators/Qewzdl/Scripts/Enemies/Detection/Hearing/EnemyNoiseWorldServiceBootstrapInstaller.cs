using Unity.Netcode;
using UnityEngine;

[DefaultExecutionOrder(-950)]
[DisallowMultipleComponent]
public sealed class EnemyNoiseWorldServiceBootstrapInstaller : MonoBehaviour
{
    [SerializeField] private EnemyNoiseWorldService noiseWorldService;

    private bool missingNoiseWorldServiceLogged;
    private bool missingProjectContextLogged;
    private bool missingNetworkManagerLogged;

    private void Awake()
    {
        Install();
    }

    public bool Install()
    {
        if (!ValidateNoiseWorldService())
        {
            return false;
        }

        ProjectContext context = ProjectContext.Instance;

        if (context == null)
        {
            LogMissingProjectContext();
            noiseWorldService.enabled = false;
            return false;
        }

        NetworkManager networkManager = context.NetworkManager;

        if (networkManager == null)
        {
            LogMissingNetworkManager();
            noiseWorldService.enabled = false;
            return false;
        }

        missingProjectContextLogged = false;
        missingNetworkManagerLogged = false;

        return noiseWorldService.Construct(networkManager);
    }

    private bool ValidateNoiseWorldService()
    {
        if (noiseWorldService != null)
        {
            missingNoiseWorldServiceLogged = false;
            return true;
        }

        if (!missingNoiseWorldServiceLogged)
        {
            missingNoiseWorldServiceLogged = true;

            Debug.LogError(
                $"{nameof(EnemyNoiseWorldServiceBootstrapInstaller)} requires {nameof(EnemyNoiseWorldService)}.",
                this
            );
        }

        return false;
    }

    private void LogMissingProjectContext()
    {
        if (missingProjectContextLogged)
        {
            return;
        }

        missingProjectContextLogged = true;

        Debug.LogError(
            $"{nameof(EnemyNoiseWorldServiceBootstrapInstaller)} requires active {nameof(ProjectContext)} " +
            "from Bootstrap scene before installing enemy hearing services.",
            this
        );
    }

    private void LogMissingNetworkManager()
    {
        if (missingNetworkManagerLogged)
        {
            return;
        }

        missingNetworkManagerLogged = true;

        Debug.LogError(
            $"{nameof(EnemyNoiseWorldServiceBootstrapInstaller)} requires {nameof(ProjectContext)} " +
            $"with assigned {nameof(NetworkManager)}.",
            this
        );
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (noiseWorldService == null)
        {
            noiseWorldService = GetComponent<EnemyNoiseWorldService>();
        }
    }
#endif
}