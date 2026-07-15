using System;
using System.Collections.Generic;
using UnityEngine;

internal readonly struct ProjectSceneScopeRequirements
{
    private readonly Type requiredFeatureType;

    internal ProjectSceneScopeRequirements(
        ProjectSceneKind sceneKind,
        SceneServiceScopeParent parent,
        Type featureType)
    {
        SceneKind = sceneKind;
        Parent = parent;
        requiredFeatureType = featureType;
    }

    internal ProjectSceneKind SceneKind { get; }
    internal SceneServiceScopeParent Parent { get; }
    internal bool RequiresSceneRuntime => requiredFeatureType != null;
    internal string RequiredFeatureName => requiredFeatureType?.Name;

    internal bool ValidateConfiguredFeatures(
        IReadOnlyList<SceneRuntimeFeature> features,
        string sceneLabel)
    {
        if (requiredFeatureType == null)
            return true;

        if (features != null)
        {
            for (int i = 0; i < features.Count; i++)
            {
                SceneRuntimeFeature feature = features[i];

                if (feature != null && requiredFeatureType.IsInstanceOfType(feature))
                    return true;
            }
        }

        Debug.LogError(
            $"Scene '{sceneLabel}' ({SceneKind}) requires feature " +
            $"'{requiredFeatureType.Name}', but it is not configured.");

        return false;
    }

    internal bool ValidateReadyServices(ServiceScope services, string sceneLabel)
    {
        if (services == null || services.IsDisposed)
        {
            Debug.LogError(
                $"Cannot validate required contracts for inactive scene scope '{sceneLabel}'.");

            return false;
        }

        bool valid = true;

        switch (SceneKind)
        {
            case ProjectSceneKind.Lobby:
                valid &= RequireLocal<ILobbyReadService>(services, sceneLabel);
                valid &= RequireLocal<ILobbyCommandService>(services, sceneLabel);
                break;

            case ProjectSceneKind.Game:
                valid &= RequireLocal<IPauseService>(services, sceneLabel);
                break;
        }

        return valid;
    }

    private static bool RequireLocal<TContract>(ServiceScope services, string sceneLabel)
        where TContract : class
    {
        if (services.HasLocalRegistration<TContract>())
            return true;

        Debug.LogError(
            $"Scene scope '{sceneLabel}' is missing required local contract " +
            $"'{typeof(TContract).Name}'.");

        return false;
    }
}

internal static class ProjectSceneScopePolicy
{
    internal static bool TryGetRequirements(
        ProjectSceneKind sceneKind,
        bool isGameMapScene,
        out ProjectSceneScopeRequirements requirements)
    {
        switch (sceneKind)
        {
            case ProjectSceneKind.MainMenu:
                requirements = new ProjectSceneScopeRequirements(
                    sceneKind,
                    SceneServiceScopeParent.Global,
                    typeof(MainMenuSceneFeature));
                return true;

            case ProjectSceneKind.Lobby:
                requirements = new ProjectSceneScopeRequirements(
                    sceneKind,
                    SceneServiceScopeParent.Session,
                    typeof(LobbySceneFeature));
                return true;

            case ProjectSceneKind.Game:
                requirements = new ProjectSceneScopeRequirements(
                    sceneKind,
                    SceneServiceScopeParent.Session,
                    typeof(GameSceneFeature));
                return true;
        }

        if (isGameMapScene)
        {
            requirements = new ProjectSceneScopeRequirements(
                sceneKind,
                SceneServiceScopeParent.Session,
                null);
            return true;
        }

        requirements = default;
        return false;
    }
}
