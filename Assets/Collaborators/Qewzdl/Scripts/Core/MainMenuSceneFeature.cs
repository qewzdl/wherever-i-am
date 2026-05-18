using UnityEngine;

public sealed class MainMenuSceneFeature : SceneRuntimeFeature
{
    [SerializeField] private MainMenuUI mainMenu;

    public override void Install(ProjectContext context)
    {
        if (context == null || mainMenu == null)
            return;

        mainMenu.Construct(context.SessionService, context.UiErrors);
    }
}
