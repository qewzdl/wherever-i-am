using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProjectSceneLoadExecutor : MonoBehaviour
{
    [SerializeField] private ProjectSceneNavigator sceneNavigator;
    [SerializeField] private NetworkManager networkManager;

    public void Construct(ProjectSceneNavigator navigator, NetworkManager manager)
    {
        sceneNavigator = navigator;
        networkManager = manager;
    }

    public void DisposeComposition()
    {
        sceneNavigator = null;
        networkManager = null;
    }

    public bool ShouldTrackNetworkCompletion(ProjectSceneTransitionDefinition transition)
    {
        if (!HasRequiredReferences())
            return false;

        return transition.LoadMode == ProjectSceneLoadMode.Network && networkManager.IsListening;
    }

    public bool Load(ProjectSceneDefinition scene, ProjectSceneTransitionDefinition transition)
    {
        if (!HasRequiredReferences())
            return false;

        switch (transition.LoadMode)
        {
            case ProjectSceneLoadMode.Local:
                return LoadLocalScene(scene);

            case ProjectSceneLoadMode.Network:
                return LoadNetworkScene(scene, transition);
        }

        Debug.LogError(
            $"Unsupported scene load mode '{transition.LoadMode}' for transition {transition.FromScene} -> {transition.ToScene}.",
            this);

        return false;
    }

    private bool LoadLocalScene(ProjectSceneDefinition scene)
    {
        if (networkManager.IsListening)
        {
            Debug.LogError(
                $"Cannot load local scene '{scene.Kind}' while NetworkManager is listening. Shutdown network session first.",
                this);

            return false;
        }

        return sceneNavigator.Load(scene.Kind, ProjectSceneLoadMode.Local);
    }

    private bool LoadNetworkScene(
        ProjectSceneDefinition scene,
        ProjectSceneTransitionDefinition transition)
    {
        if (!networkManager.IsListening)
        {
#if UNITY_EDITOR
            if (transition.AllowEditorDirectLoad)
                return sceneNavigator.Load(scene.Kind, ProjectSceneLoadMode.Local);
#endif

            Debug.LogError(
                $"Cannot load network scene '{scene.Kind}' without an active network session.",
                this);

            return false;
        }

        return sceneNavigator.Load(scene.Kind, ProjectSceneLoadMode.Network);
    }

    private bool HasRequiredReferences()
    {
        bool valid = true;

        valid &= ValidateRequiredReference(sceneNavigator, nameof(sceneNavigator));
        valid &= ValidateRequiredReference(networkManager, nameof(networkManager));

        return valid;
    }

    private bool ValidateRequiredReference(Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError($"{nameof(ProjectSceneLoadExecutor)} is missing '{fieldName}'.", this);
        return false;
    }
}
