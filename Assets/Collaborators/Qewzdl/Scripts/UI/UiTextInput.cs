using UnityEngine.UIElements;

// What every text field in this game has to be told before it behaves.
//
// A keyboard shortcut reaches a field twice. Once as a key code with a
// modifier, which the field turns into a command - select all, copy, cut,
// paste - and once as the control character that letter stands for: Ctrl+A is
// also U+0001, Ctrl+V is also U+0016. Nothing filters the second one, so it is
// typed. Select all lights the whole message up and the U+0001 immediately
// behind it replaces the selection, which is why Ctrl+A empties the box
// instead of highlighting it. The rest do the same damage more quietly - a cut
// or a paste leaves an invisible character sitting in the text.
//
// So the stray half is stopped on the way down, before the field's own editor
// sees it. The command half is a different event entirely and is untouched:
// select all, copy, cut and paste all still work, and now only work.
internal static class UiTextInput
{
    internal static void Guard(TextField field)
    {
        if (field == null)
            return;

        field.UnregisterCallback<KeyDownEvent>(HandleKeyDown, TrickleDown.TrickleDown);
        field.RegisterCallback<KeyDownEvent>(HandleKeyDown, TrickleDown.TrickleDown);
    }

    private static void HandleKeyDown(KeyDownEvent evt)
    {
        if (IsStrayControlCharacter(evt.character))
            evt.StopImmediatePropagation();
    }

    // Nought is not a control character, it is the absence of one: every key
    // that carries no letter - the arrows, Home, Delete, Escape - arrives with
    // it, and swallowing those would leave a field nothing can be moved around
    // in.
    //
    // The four that are kept are the ones a text field is entitled to act on
    // as characters rather than as commands.
    private static bool IsStrayControlCharacter(char character)
    {
        if (character == '\0' ||
            character == '\b' ||
            character == '\t' ||
            character == '\n' ||
            character == '\r')
        {
            return false;
        }

        return character < ' ' || character == '\u007f';
    }
}
