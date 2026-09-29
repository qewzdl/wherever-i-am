using UnityEngine;
using UnityEngine.UIElements;

// A ScrollView the mouse wheel moves a fixed step per notch: one line of
// text, and never more than half of what is on screen.
//
// Left to itself a ScrollView multiplies each notch by its own wheel size,
// which in a view a few lines tall moved the text by most of a screen at a
// time, and the lines in between were never shown at all. The notch is taken
// as one step whatever size the platform reports it at, so what one notch
// does cannot depend on the input backend.
public static class WheelStepScroll
{
    public static void Attach(ScrollView view, float step)
    {
        if (view == null)
            return;

        view.RegisterCallback<WheelEvent>(
            evt => OnWheel(view, step, evt),
            TrickleDown.TrickleDown);
    }

    private static void OnWheel(ScrollView view, float step, WheelEvent evt)
    {
        if (!Mathf.Approximately(evt.delta.y, 0f))
            Step(view, step, Mathf.Sign(evt.delta.y));

        // Taken here, so the ScrollView's own wheel handling never sees it.
        evt.StopImmediatePropagation();
    }

    // One notch: positive is down, towards newer lines.
    public static void Step(ScrollView view, float step, float direction)
    {
        // How far there is to scroll, as the ScrollView itself measures it.
        // Not the content container's height: that is the viewport's, whatever
        // is inside it.
        float furthest = view.verticalScroller.highValue;

        if (furthest <= 0f)
            return;

        float visible = view.contentViewport.layout.height;
        float move = Mathf.Min(step, visible * 0.5f) * direction;

        view.scrollOffset = new Vector2(
            view.scrollOffset.x,
            Mathf.Clamp(view.scrollOffset.y + move, 0f, furthest));
    }
}
