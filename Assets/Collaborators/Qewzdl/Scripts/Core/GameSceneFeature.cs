using System.Collections.Generic;
using UnityEngine;

public sealed class GameSceneFeature : SceneRuntimeFeature
{
    [SerializeField] private SceneRuntimeFeature[] features;

    private SceneRuntimeFeature[] installedFeatures;
    private int installedFeatureCount;

    protected override bool ValidateFeature(SceneFeatureContext context)
    {
        SceneRuntimeFeature[] sceneFeatures = ResolveFeatures();

        if (sceneFeatures.Length == 0)
        {
            Debug.LogError($"{nameof(GameSceneFeature)} has no gameplay features.", this);
            return false;
        }

        HashSet<SceneRuntimeFeature> uniqueFeatures = new();
        bool valid = true;

        for (int i = 0; i < sceneFeatures.Length; i++)
        {
            SceneRuntimeFeature feature = sceneFeatures[i];

            if (feature == null)
            {
                LogMissingReference($"{nameof(features)}[{i}]");
                valid = false;
                continue;
            }

            if (feature == this)
            {
                Debug.LogError($"{nameof(GameSceneFeature)} cannot install itself.", this);
                valid = false;
                continue;
            }

            if (!uniqueFeatures.Add(feature))
            {
                Debug.LogError(
                    $"Gameplay feature '{feature.GetType().Name}' is registered more than once.",
                    feature);

                valid = false;
                continue;
            }

            if (!feature.Validate(context))
                valid = false;
        }

        return valid;
    }

    protected override bool InstallFeature(SceneFeatureContext context)
    {
        installedFeatures = ResolveFeatures();
        installedFeatureCount = 0;

        for (int i = 0; i < installedFeatures.Length; i++)
        {
            SceneRuntimeFeature feature = installedFeatures[i];

            if (feature != null && feature.Install(context))
            {
                installedFeatureCount++;
                continue;
            }

            Debug.LogError(
                $"{nameof(GameSceneFeature)} failed to install gameplay feature " +
                $"'{(feature != null ? feature.GetType().Name : "Missing")}'.",
                feature);

            UninstallInstalledFeatures();
            return false;
        }

        return true;
    }

    protected override void UninstallFeature(SceneFeatureContext context)
    {
        UninstallInstalledFeatures();
    }

    private void UninstallInstalledFeatures()
    {
        if (installedFeatures == null)
            return;

        for (int i = installedFeatureCount - 1; i >= 0; i--)
        {
            SceneRuntimeFeature feature = installedFeatures[i];

            if (feature != null)
                feature.Uninstall();
        }

        installedFeatureCount = 0;
        installedFeatures = null;
    }

    private SceneRuntimeFeature[] ResolveFeatures()
    {
        if (features != null && features.Length > 0)
            return features;

        return GetChildFeatures();
    }

    private SceneRuntimeFeature[] GetChildFeatures()
    {
        SceneRuntimeFeature[] childFeatures = GetComponentsInChildren<SceneRuntimeFeature>(true);
        int featureCount = 0;

        for (int i = 0; i < childFeatures.Length; i++)
        {
            if (childFeatures[i] != null && childFeatures[i] != this)
                featureCount++;
        }

        SceneRuntimeFeature[] resolvedFeatures = new SceneRuntimeFeature[featureCount];
        int nextFeatureIndex = 0;

        for (int i = 0; i < childFeatures.Length; i++)
        {
            SceneRuntimeFeature feature = childFeatures[i];

            if (feature == null || feature == this)
                continue;

            resolvedFeatures[nextFeatureIndex] = feature;
            nextFeatureIndex++;
        }

        return resolvedFeatures;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (features == null || features.Length == 0)
            features = GetChildFeatures();
    }
#endif
}
