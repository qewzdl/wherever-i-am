using TMPro;
using UnityEngine;

// The last thing a match says before everyone is taken back to the lobby. It
// waits for the flow rather than resolving once: the match object spawns over
// the network and can arrive after this scene is already standing.
public sealed class MatchResultUi : SceneRuntimeFeature
{
    [Header("UI")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text resultText;

    [Header("Text")]
    [SerializeField] private string victoryText = "You got out";
    [SerializeField] private string defeatText = "Everyone was caught";
    [SerializeField] private string drawText = "The match is over";

    private ISessionServiceRegistry serviceRegistry;
    private IMatchCompletionService matchService;

    protected override bool ValidateFeature(SceneFeatureContext context)
    {
        bool valid = true;
        valid &= RequireReference(panel, nameof(panel));
        valid &= RequireReference(resultText, nameof(resultText));
        valid &= RequireService<ISessionServiceRegistry>(context, out _);
        return valid;
    }

    protected override bool InstallFeature(SceneFeatureContext context)
    {
        serviceRegistry = context.Services.Resolve<ISessionServiceRegistry>();
        serviceRegistry.ServicesChanged += RefreshBinding;

        SetVisible(false);
        RefreshBinding();
        return true;
    }

    protected override void UninstallFeature(SceneFeatureContext context)
    {
        if (serviceRegistry != null)
            serviceRegistry.ServicesChanged -= RefreshBinding;

        Unbind();
        serviceRegistry = null;
    }

    private void RefreshBinding()
    {
        IMatchCompletionService resolved = null;
        serviceRegistry?.TryResolve(out resolved);

        if (matchService == resolved)
            return;

        Unbind();
        matchService = resolved;

        if (matchService == null)
            return;

        matchService.MatchResolved += HandleMatchResolved;

        // Already over by the time this bound - rare, but silence would be the
        // very thing this exists to prevent.
        if (matchService.CurrentResult.HasResult)
            HandleMatchResolved(matchService.CurrentResult);
    }

    private void Unbind()
    {
        if (matchService == null)
            return;

        matchService.MatchResolved -= HandleMatchResolved;
        matchService = null;
    }

    private void HandleMatchResolved(GameResultData matchResult)
    {
        if (resultText != null)
        {
            resultText.text = matchResult.ResultType switch
            {
                GameResultType.Victory => victoryText,
                GameResultType.Defeat => defeatText,
                _ => drawText
            };
        }

        SetVisible(true);
    }

    private void SetVisible(bool visible)
    {
        if (panel != null)
            panel.SetActive(visible);
    }
}
