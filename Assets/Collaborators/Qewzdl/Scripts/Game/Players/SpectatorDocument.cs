using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// What a caught player is told while they watch somebody else play, in UI
// Toolkit.
//
// It reads PlayerSpectatorView rather than being handed to it, the way the
// uGUI panel it replaces did: that view is added to the player at runtime, when
// they are caught, and cannot be wired to anything sitting in a scene.
//
// What it says grew. The old panel named whoever the camera was following and
// counted the living - "Watching Alex", "3 still alive" - and stopped there. A
// count is the least useful way to describe the three people it is counting,
// and neither line mentioned the only two things a spectator can actually do.
// So the count became the names, with the watched one lit, and the two buttons
// that move between them are written down.
[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class SpectatorDocument : MonoBehaviour
{
    private const string WatchingClass = "spectate--watching";
    private const string NobodyClass = "spectate__watching--nobody";
    private const string SurvivorClass = "spectate__survivor";
    private const string WatchedClass = "spectate__survivor--watched";
    private const string NextClass = "spectate__survivor--next";

    [Header("References")]
    [SerializeField] private UIDocument document;

    [Header("Text")]
    [SerializeField] private string watchingFormat = "Watching {0}";
    [SerializeField] private string nobodyToWatchText = "Nobody left to watch";

    private VisualElement boundRoot;
    private VisualElement layer;
    private Label watchingLabel;
    private VisualElement survivors;
    private Label nextKeyLabel;
    private Label previousKeyLabel;

    private readonly List<PlayerEnemyAttackReceiver> shown = new();

    private bool isWatching;
    private PlayerEnemyAttackReceiver reportedWatched;
    private PlayerEnemyAttackReceiver reportedNext;
    private string reportedNextKey;
    private string reportedPreviousKey;

    private void Awake()
    {
        if (document == null)
            document = GetComponent<UIDocument>();

        DetachFromParentDocument();
    }

    // A UIDocument that finds another one above it in the hierarchy stops being
    // a panel of its own: it is grafted into that document's tree and its own
    // settings are ignored. This one is added to the HUD, which is a uGUI
    // object today and may not always be.
    private void DetachFromParentDocument()
    {
        Transform parent = transform.parent;

        if (parent == null || parent.GetComponentInParent<UIDocument>(true) == null)
            return;

        transform.SetParent(null, false);
    }

    private void OnEnable()
    {
        if (Bind())
            SetWatching(false);
    }

    // Polled rather than subscribed to, because the thing it is watching does
    // not announce itself: PlayerSpectatorView is attached at the moment its
    // player is caught, and stops existing when the match ends.
    private void LateUpdate()
    {
        if (!Bind())
            return;

        PlayerSpectatorView spectator = PlayerSpectatorView.Current;

        if (spectator == null)
        {
            SetWatching(false);
            return;
        }

        SetWatching(true);
        RefreshKeys();
        RefreshWatched(spectator.Watched);
        RefreshSurvivors(PlayerEnemyAttackReceiver.All, spectator.Watched);
    }

    // Asked of the class that reads the buttons, so the prompt cannot end up
    // naming one thing while another does the work. It follows the device the
    // player last touched: a pad in the hands and a label saying LMB is a
    // label arguing with the player about what they are holding.
    private void RefreshKeys()
    {
        string next = PlayerSpectatorView.NextButtonName;
        string previous = PlayerSpectatorView.PreviousButtonName;

        if (nextKeyLabel != null && reportedNextKey != next)
        {
            reportedNextKey = next;
            nextKeyLabel.text = next;
        }

        if (previousKeyLabel != null && reportedPreviousKey != previous)
        {
            reportedPreviousKey = previous;
            previousKeyLabel.text = previous;
        }
    }

    private bool Bind()
    {
        if (document == null)
            document = GetComponent<UIDocument>();

        VisualElement root = document != null ? document.rootVisualElement : null;

        if (root == null)
            return false;

        if (ReferenceEquals(root, boundRoot))
            return layer != null;

        boundRoot = root;
        UiPreferences.Attach(root);

        layer = root.Q<VisualElement>("Spectate");
        watchingLabel = root.Q<Label>("Watching");
        survivors = root.Q<VisualElement>("Survivors");
        nextKeyLabel = root.Q<Label>("NextKey");
        previousKeyLabel = root.Q<Label>("PreviousKey");

        return layer != null;
    }

    private void SetWatching(bool watching)
    {
        if (isWatching == watching)
            return;

        isWatching = watching;
        layer?.EnableInClassList(WatchingClass, watching);

        if (watching)
            return;

        // A match that ends while somebody is watching leaves this holding the
        // last thing it said. Forgotten now, so the next one starts empty.
        reportedWatched = null;
        reportedNext = null;
        shown.Clear();
        survivors?.Clear();
    }

    // Only when it actually changed: this runs every frame, and a label
    // rewritten every frame is a layout rebuilt every frame.
    private void RefreshWatched(PlayerEnemyAttackReceiver watched)
    {
        if (reportedWatched == watched || watchingLabel == null)
            return;

        reportedWatched = watched;

        string playerName = watched != null ? watched.DisplayName : string.Empty;
        bool hasName = !string.IsNullOrEmpty(playerName);

        watchingLabel.text = hasName
            ? string.Format(watchingFormat, playerName)
            : nobodyToWatchText;

        watchingLabel.EnableInClassList(NobodyClass, !hasName);
    }

    // The list is rebuilt only when the people in it change, which is when
    // somebody is caught - a handful of times a match, rather than sixty times
    // a second. Nothing in it can be focused, so there is nothing to lose by
    // rebuilding it when it does change.
    private void RefreshSurvivors(
        IReadOnlyList<PlayerEnemyAttackReceiver> players,
        PlayerEnemyAttackReceiver watched)
    {
        if (survivors == null)
            return;

        PlayerEnemyAttackReceiver next = NextInRotation(players, watched);

        if (!HasSurvivorListChanged(players))
        {
            Mark(watched, next);
            return;
        }

        shown.Clear();
        survivors.Clear();

        for (int i = 0; i < players.Count; i++)
        {
            PlayerEnemyAttackReceiver player = players[i];

            if (player == null || player.IsEliminated)
                continue;

            shown.Add(player);

            Label name = new Label(player.DisplayName) { enableRichText = false };
            name.AddToClassList(SurvivorClass);
            survivors.Add(name);
        }

        Mark(watched, next);
    }

    // Whoever the Next button would land on, asked of the very function that
    // button calls. A list that worked this out for itself would be a second
    // opinion about the rotation, and the day the two disagree is the day the
    // list starts lying about where the button goes.
    private PlayerEnemyAttackReceiver NextInRotation(
        IReadOnlyList<PlayerEnemyAttackReceiver> players,
        PlayerEnemyAttackReceiver watched)
    {
        PlayerSpectatorView spectator = PlayerSpectatorView.Current;
        PlayerEnemyAttackReceiver self = spectator != null ? spectator.Self : null;

        PlayerEnemyAttackReceiver next =
            PlayerSpectatorView.NextTarget(players, self, watched);

        // With one survivor left, Next comes back to the person already being
        // watched. Marking them twice says nothing.
        return next == watched ? null : next;
    }

    private bool HasSurvivorListChanged(IReadOnlyList<PlayerEnemyAttackReceiver> players)
    {
        if (players == null)
            return shown.Count != 0;

        int alive = 0;

        for (int i = 0; i < players.Count; i++)
        {
            PlayerEnemyAttackReceiver player = players[i];

            if (player == null || player.IsEliminated)
                continue;

            if (alive >= shown.Count || shown[alive] != player)
                return true;

            alive++;
        }

        return alive != shown.Count;
    }

    // Three steps of the same ladder rather than three ideas: the one being
    // watched, the one the button leads to, and everybody else. The list stays
    // in its own order - a list that reorders itself as you cycle is a list
    // nobody can keep their place in - and says where the next press goes.
    private void Mark(PlayerEnemyAttackReceiver watched, PlayerEnemyAttackReceiver next)
    {
        if (reportedWatched == watched && reportedNext == next)
            return;

        reportedNext = next;

        for (int i = 0; i < shown.Count && i < survivors.childCount; i++)
        {
            survivors[i].EnableInClassList(WatchedClass, shown[i] == watched);
            survivors[i].EnableInClassList(NextClass, shown[i] == next);
        }
    }
}
