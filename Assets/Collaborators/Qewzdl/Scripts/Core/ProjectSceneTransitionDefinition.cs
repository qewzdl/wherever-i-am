using System;
using UnityEngine;

[Serializable]
public struct ProjectSceneTransitionDefinition
{
    [SerializeField] private ProjectSceneKind fromScene;
    [SerializeField] private ProjectSceneKind toScene;
    [SerializeField] private ProjectSceneLoadMode loadMode;
    [SerializeField] private ProjectSceneTransitionAuthority authority;
    [SerializeField] private bool requiresActiveNetworkSession;
    [SerializeField] private bool allowEditorDirectLoad;
    [SerializeField] private GameState stateBeforeLoad;
    [SerializeField] private ProjectSceneServerAction[] serverActionsAfterLoad;

    public ProjectSceneTransitionDefinition(
        ProjectSceneKind fromScene,
        ProjectSceneKind toScene,
        ProjectSceneLoadMode loadMode,
        ProjectSceneTransitionAuthority authority,
        bool requiresActiveNetworkSession,
        bool allowEditorDirectLoad,
        GameState stateBeforeLoad,
        params ProjectSceneServerAction[] serverActionsAfterLoad)
    {
        this.fromScene = fromScene;
        this.toScene = toScene;
        this.loadMode = loadMode;
        this.authority = authority;
        this.requiresActiveNetworkSession = requiresActiveNetworkSession;
        this.allowEditorDirectLoad = allowEditorDirectLoad;
        this.stateBeforeLoad = stateBeforeLoad;
        this.serverActionsAfterLoad = serverActionsAfterLoad;
    }

    public ProjectSceneKind FromScene => fromScene;
    public ProjectSceneKind ToScene => toScene;
    public ProjectSceneLoadMode LoadMode => loadMode;
    public ProjectSceneTransitionAuthority Authority => authority;
    public bool RequiresActiveNetworkSession => requiresActiveNetworkSession;
    public bool AllowEditorDirectLoad => allowEditorDirectLoad;
    public GameState StateBeforeLoad => stateBeforeLoad;
    public ProjectSceneServerAction[] ServerActionsAfterLoad => serverActionsAfterLoad;

    public bool IsExactMatch(ProjectSceneKind currentScene, ProjectSceneKind targetScene)
    {
        return fromScene == currentScene && toScene == targetScene;
    }

    public bool IsWildcardMatch(ProjectSceneKind targetScene)
    {
        return fromScene == ProjectSceneKind.Unknown && toScene == targetScene;
    }
}