using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class ChatTypographyApplier
{
    public static void Apply(GameObject root, ChatTypographyProfile profile)
    {
        if (root == null)
        {
            return;
        }

        ChatMessageListView[] messageLists = root.GetComponentsInChildren<ChatMessageListView>(true);

        for (int i = 0; i < messageLists.Length; i++)
        {
            messageLists[i].SetTypographyProfile(profile);
        }

        ApplyToTexts(root, profile);
        ApplyToScrollbars(root, profile);
    }

    // The lobby window and the one inside the phone are the same prefab, so the
    // handle is coloured wherever that prefab ends up. The scrollbar's own state
    // colours multiply on top, which keeps hover and drag readable.
    private static void ApplyToScrollbars(GameObject root, ChatTypographyProfile profile)
    {
        if (root == null || profile == null)
        {
            return;
        }

        Scrollbar[] scrollbars =
            root.GetComponentsInChildren<Scrollbar>(profile.IncludeInactiveText);

        for (int i = 0; i < scrollbars.Length; i++)
        {
            Graphic handle = ResolveHandleGraphic(scrollbars[i]);

            if (handle != null)
            {
                handle.color = profile.ScrollbarHandleColor;
            }
        }
    }

    private static Graphic ResolveHandleGraphic(Scrollbar scrollbar)
    {
        if (scrollbar == null)
        {
            return null;
        }

        if (scrollbar.handleRect != null &&
            scrollbar.handleRect.TryGetComponent(out Graphic graphic))
        {
            return graphic;
        }

        return scrollbar.targetGraphic;
    }

    public static void ApplyToTexts(GameObject root, ChatTypographyProfile profile)
    {
        // A profile may carry only a colour, only a font, or both - each is
        // worth applying on its own.
        if (root == null || profile == null)
        {
            return;
        }

        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(profile.IncludeInactiveText);

        for (int i = 0; i < texts.Length; i++)
        {
            ApplyToText(texts[i], profile);
        }
    }

    private static void ApplyToText(TMP_Text text, ChatTypographyProfile profile)
    {
        if (text == null)
        {
            return;
        }

        if (profile.HasFont)
        {
            text.font = profile.FontAsset;

            if (profile.FontSharedMaterial != null)
            {
                text.fontSharedMaterial = profile.FontSharedMaterial;
            }
        }

        text.color = profile.TextColor;

        text.SetAllDirty();
    }
}
