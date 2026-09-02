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
                if (element.ClassListContains(openClass))
                    return;

                element.style.display = DisplayStyle.None;
                ForgetThePointer(element);
            })
            .StartingIn(FadeMilliseconds);
    }

    // A dialog is usually closed by pressing something inside it, which means
    // the pointer is sitting on that button at the moment the panel goes away.
    // Nothing leaves anything: the element does not move out from under the
    // cursor, it stops being drawn, so no PointerLeave is ever sent and the
    // hover flag stays set on the button. Open the dialog again and its Cancel
    // arrives already lit, and stays lit until a real enter and leave clear it -
    // which is why hovering the button once fixes it.
    //
    // Taking the panel out of the tree and putting it straight back is what
    // does send the leave: display is a paint decision and the panel keeps its
    // place under the pointer, while a detach is the element genuinely going
    // away. The index is kept because these are overlays, and which one is on
    // top of which is decided by sibling order.
    private static void ForgetThePointer(VisualElement element)
    {
        VisualElement parent = element.parent;

        if (parent == null)
            return;

        int index = parent.IndexOf(element);

        if (index < 0)
            return;

        element.RemoveFromHierarchy();
        parent.Insert(index, element);
    }
}
