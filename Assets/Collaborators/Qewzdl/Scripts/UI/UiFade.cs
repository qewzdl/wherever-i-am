using UnityEngine.UIElements;

// Showing a thing that fades, in the two steps UI Toolkit actually requires.
//
// display has to stay in the picture: an element at zero opacity still lays
// out and still swallows every click aimed at what is behind it. But a class
// added in the same frame the element appears never transitions - there is no
// resolved style to leave from - so the class waits a frame, and on the way
// out display waits for the fade instead of cutting it in half.
internal static class UiFade
{
    // Longer than --motion-normal, not equal to it. A hide that lands on the
    // same millisecond as the fade is a coin toss about which the player sees.
    private const long FadeMilliseconds = 200;

    internal static void Set(VisualElement element, bool visible, string openClass)
    {
        if (element == null)
            return;

        if (visible)
        {
            element.style.display = DisplayStyle.Flex;
            element.schedule.Execute(() => element.AddToClassList(openClass));
            return;
        }

        element.RemoveFromClassList(openClass);

        // Checked on arrival rather than cancelled on the way in. A panel shown
        // again during its own fade out would otherwise be hidden by the timer
        // the previous close left running.
        element.schedule
            .Execute(() =>
            {
                if (!element.ClassListContains(openClass))
                    element.style.display = DisplayStyle.None;
            })
            .StartingIn(FadeMilliseconds);
    }
}
