using System;
using UnityEngine.UIElements;

// A row laid out in markup (UI/Templates) rather than put together in code.
//
// Instantiate wraps the markup in a container of its own. The row is what is
// inside it, lifted out, so a list holds rows rather than boxes that each hold
// a row - the list's styles are written for rows.
internal static class UiTemplates
{
    internal static VisualElement Stamp(VisualTreeAsset template, string what)
    {
        if (template == null)
            throw new InvalidOperationException($"No template for {what} is assigned.");

        VisualElement row = template.Instantiate()[0];
        row.RemoveFromHierarchy();
        return row;
    }
}
