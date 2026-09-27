using UnityEngine.UIElements;

// Three dots lighting in turn, for a wait that has no progress to show.
// Without something moving, a dialog that says "Connecting" reads the same
// at the second second as at the twentieth, and a stuck one looks exactly
// like a working one.
//
// It keeps its own beat for as long as it is on a panel, so any screen that
// places it - <WaitingPulse /> - gets it working with no code of its own.
// With Reduced motion there is no beat: the dots sit still, evenly lit.
[UxmlElement]
public partial class WaitingPulse : VisualElement
{
    private const int DotCount = 3;
    private const long BeatMilliseconds = 360;
    private const string LitClass = "waiting-pulse__dot--lit";

    private readonly VisualElement[] dots = new VisualElement[DotCount];
    private IVisualElementScheduledItem beat;
    private int lit;

    public WaitingPulse()
    {
        pickingMode = PickingMode.Ignore;
        AddToClassList("waiting-pulse");

        for (int i = 0; i < DotCount; i++)
        {
            dots[i] = new VisualElement { pickingMode = PickingMode.Ignore };
            dots[i].AddToClassList("waiting-pulse__dot");
            Add(dots[i]);
        }

        RegisterCallback<AttachToPanelEvent>(_ => beat = schedule.Execute(Beat).Every(BeatMilliseconds));
        RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            beat?.Pause();
            beat = null;
        });
    }

    public int LitDot => lit;

    private void Beat()
    {
        bool still = IsStill(dots[0]);
        EnableInClassList("waiting-pulse--still", still);

        for (int i = 0; i < DotCount; i++)
            dots[i].EnableInClassList(LitClass, !still && i == lit);

        if (!still)
            lit = (lit + 1) % DotCount;
    }

    // Reduced motion zeroes every duration in the theme, the dots' with them.
    private static bool IsStill(VisualElement dot)
    {
        foreach (TimeValue duration in dot.resolvedStyle.transitionDuration)
            return duration.value <= 0f;

        return false;
    }
}
