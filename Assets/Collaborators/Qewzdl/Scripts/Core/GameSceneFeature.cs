using UnityEngine;

public sealed class GameSceneFeature : SceneRuntimeFeature
{
    [SerializeField] private SceneRuntimeFeature[] features;

    protected override bool InstallFeature(ProjectContext context)
    {
        SceneRuntimeFeature[] sceneFeatures = ResolveFeatures();

        if (sceneFeatures.Length == 0)
        {
            Debug.LogError($"{nameof(GameSceneFeature)} has no gameplay features.", this);
            return false;
        }

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

            if (!feature.Install(context))
            {
                Debug.LogError(
                    $"{nameof(GameSceneFeature)} failed to install gameplay feature '{feature.GetType().Name}'.",
                    feature);

                valid = false;
            }
        }

        return valid;
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
