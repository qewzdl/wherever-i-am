using UnityEngine;

[CreateAssetMenu(menuName = "Wherever I Am/Scene Flow", fileName = "ProjectSceneFlow")]
public sealed class ProjectSceneFlow : ScriptableObject
{
    [SerializeField] private ProjectSceneTransitionDefinition[] transitions =
    {
        new ProjectSceneTransitionDefinition(
            ProjectSceneKind.Bootstrap,
            ProjectSceneKind.MainMenu,
            ProjectSceneLoadMode.Local,
            ProjectSceneTransitionAuthority.Any,
            false,
            false,
            GameState.MainMenu),

        new ProjectSceneTransitionDefinition(
            ProjectSceneKind.Unknown,
            ProjectSceneKind.MainMenu,
            ProjectSceneLoadMode.Local,
            ProjectSceneTransitionAuthority.Any,
            false,
            false,
            GameState.MainMenu),

        new ProjectSceneTransitionDefinition(
            ProjectSceneKind.MainMenu,
            ProjectSceneKind.Lobby,
            ProjectSceneLoadMode.Network,
            ProjectSceneTransitionAuthority.ServerOnly,
            true,
            false,
            GameState.Lobby,
            ProjectSceneServerAction.SpawnChatSession),

        new ProjectSceneTransitionDefinition(
            ProjectSceneKind.Lobby,
            ProjectSceneKind.Game,
            ProjectSceneLoadMode.Network,
            ProjectSceneTransitionAuthority.ServerOnly,
            true,
            false,
            GameState.LoadingGame,
            ProjectSceneServerAction.SpawnPlayers),

        new ProjectSceneTransitionDefinition(
            ProjectSceneKind.Lobby,
            ProjectSceneKind.MainMenu,
            ProjectSceneLoadMode.Local,
            ProjectSceneTransitionAuthority.Any,
            false,
            false,
            GameState.MainMenu),

        new ProjectSceneTransitionDefinition(
            ProjectSceneKind.Game,
            ProjectSceneKind.MainMenu,
            ProjectSceneLoadMode.Local,
            ProjectSceneTransitionAuthority.Any,
            false,
            false,
            GameState.MainMenu),

        new ProjectSceneTransitionDefinition(
            ProjectSceneKind.Unknown,
            ProjectSceneKind.GameplayTest,
            ProjectSceneLoadMode.Local,
            ProjectSceneTransitionAuthority.Any,
            false,
            true,
            GameState.InGame)
    };

    public bool TryGetTransition(
        ProjectSceneKind currentScene,
        ProjectSceneKind targetScene,
        out ProjectSceneTransitionDefinition transition)
    {
        if (transitions != null)
        {
            for (int i = 0; i < transitions.Length; i++)
            {
                if (!transitions[i].IsExactMatch(currentScene, targetScene))
                    continue;

                transition = transitions[i];
                return true;
            }

            for (int i = 0; i < transitions.Length; i++)
            {
                if (!transitions[i].IsWildcardMatch(targetScene))
                    continue;

                transition = transitions[i];
                return true;
            }
        }

        transition = default;
        return false;
    }
}