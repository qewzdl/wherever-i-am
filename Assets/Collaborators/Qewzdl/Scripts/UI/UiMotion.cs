using UnityEngine.UIElements;

// The two moves a list makes when something comes or goes, for any element
// whose stylesheet names a starting or a leaving state.
internal static class UiMotion
{
    // Puts the element in its starting state, which the stylesheet gives no
    // transition, and lets go of it on the next frame - so it moves from
    // there to where it rests. Also how a flash is drawn: start bright, settle.
    internal static void From(VisualElement element, string startingClass)
    {
        element.AddToClassList(startingClass);
        element.schedule.Execute(() => element.RemoveFromClassList(startingClass));
    }

    // Plays the element's leaving state and takes it out once that is done.
    // Switched off at once, so it cannot be pressed on its way out.
    internal static void Out(VisualElement element, string leavingClass, long milliseconds)
    {
        element.SetEnabled(false);
        element.AddToClassList(leavingClass);
        element.schedule.Execute(element.RemoveFromHierarchy).StartingIn(milliseconds);
    }
}
