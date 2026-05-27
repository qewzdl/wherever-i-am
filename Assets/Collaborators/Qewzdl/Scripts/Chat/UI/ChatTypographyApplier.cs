using TMPro;
using UnityEngine;

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
    }

    public static void ApplyToTexts(GameObject root, ChatTypographyProfile profile)
    {
        if (root == null || profile == null || !profile.HasFont)
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

        text.font = profile.FontAsset;

        if (profile.FontSharedMaterial != null)
        {
            text.fontSharedMaterial = profile.FontSharedMaterial;
        }

        text.SetAllDirty();
    }
}
