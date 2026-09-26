using UnityEngine;
using UnityEngine.UIElements;

// The last thing a match says before everyone is taken back to the lobby.
//
// It waits for the flow rather than resolving once: the match object spawns
// over the network and can arrive after this scene is already standing.
//
// This was a TextMeshPro label on a canvas of its own, left behind when the
// rest of the interface moved to UI Toolkit - a different font, a different
// scale and a different set of colours from every other screen in the game, on
// the one screen a player is guaranteed to read.
public sealed class MatchResultUi : SceneRuntimeFeature
{
    private const string OpenClass = "screen--open";
    private const string DefeatClass = "result--defeat";

    [Header("UI")]
    [SerializeField] private UIDocument document;

    [Header("Text")]
    [SerializeField] private string victoryText = "You got out";
    [SerializeField] private string defeatText = "Everyone was caught";

    // Alone, "everyone" is one person.
    [SerializeField] private string aloneDefeatText = "You were caught";

    // Lost without anybody being caught - the way out stayed shut. Saying
    // somebody was caught would be describing a different match.
    [SerializeField] private string notOutDefeatText = "You did not get out";
    [SerializeField] private string drawText = "The match is over";

    // What happens next, under the outcome. "Everyone" and "the lobby" are
    // both about other people; alone there is only the next match.
    [SerializeField] private string groupNoteText = "Everyone is going back to the lobby.";
    [SerializeField] private string aloneNoteText = "Getting ready for the next match.";

    private ISessionServiceRegistry serviceRegistry;
    private IMatchCompletionService matchService;
    private VisualElement screen;
    private Label outcome;
    private Label note;

    protected override bool ValidateFeature(SceneFeatureContext context)
    {
        bool valid = true;
        valid &= RequireReference(document, nameof(document));
        valid &= RequireService<ISessionServiceRegistry>(context, out _);
        return valid;
    }

    protected override bool InstallFeature(SceneFeatureContext context)
    {
        if (!Bind())
            return false;

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

    private bool Bind()
    {
        VisualElement root = document != null ? document.rootVisualElement : null;

        if (root == null)
        {
            Debug.LogError($"{nameof(MatchResultUi)} has no document to bind.", this);
            return false;
        }

        // Scale, text size and the player's motion setting, the same as every
        // other tree gets, and then the language. A screen that skipped these
        // would be the one screen that ignored the settings.
        UiPreferences.Attach(root);
        UiLocalization.Apply(root);

        screen = root.Q<VisualElement>("Screen");
        outcome = root.Q<Label>("Outcome");
        note = root.Q<Label>("Note");

        if (screen != null && outcome != null)
            return true;

        Debug.LogError($"{nameof(MatchResultUi)} is missing an element.", this);
        return false;
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
        bool alone = PlayerEnemyAttackReceiver.PlayedAlone(PlayerEnemyAttackReceiver.All);

        outcome.text = UiLocalization.Text(matchResult.ResultType switch
        {
            GameResultType.Victory => victoryText,
            GameResultType.Defeat => matchResult.Source != MatchResultSource.PlayerCaught
                ? notOutDefeatText
                : alone ? aloneDefeatText : defeatText,
            _ => drawText
        });

        UiLocalization.SetText(note, alone ? aloneNoteText : groupNoteText);

        // Only the loss is coloured. Rust is the one place this palette raises
        // its voice, and everybody being caught is the one thing in a match
        // worth raising it for.
        screen.EnableInClassList(DefeatClass, matchResult.ResultType == GameResultType.Defeat);

        SetVisible(true);
    }

    private void SetVisible(bool visible)
    {
        if (screen == null)
            return;

        if (!visible)
        {
            screen.RemoveFromClassList(OpenClass);
            screen.style.display = DisplayStyle.None;
            return;
        }

        screen.style.display = DisplayStyle.Flex;

        // A class added in the same frame the element is shown never
        // transitions: there is no resolved style to leave from, and the
        // screen would simply be there rather than arriving.
        screen.schedule.Execute(() => screen.AddToClassList(OpenClass));
    }
}
