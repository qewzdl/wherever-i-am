using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;

// Keeping generated glyphs out of the repository.
//
// The fonts fill their atlases on demand: draw a letter that is not in one yet
// and it is rendered, packed and written into the asset. That is what makes a
// dynamic atlas worth having - nobody has to predict which characters a player
// name or a translation will need - and it means the asset changes every time
// the editor draws a word it has not drawn before.
//
// Those assets are megabytes of packed texture. Vectipede was three and a
// third, Freeride two and a quarter, and both grew a little on every commit
// that happened to have rendered something new. Somebody decided once that
// they should be committed empty; this is what makes that happen rather than
// something to remember before every commit.
//
// Nothing is lost by clearing them. Every glyph in there was generated from a
// font file that is also in the repository, and will be generated again the
// moment anybody looks at a word that needs it.
public static class FontAtlasHousekeeping
{
    private const string FontsFolder = "Assets/Collaborators/Qewzdl/Fonts";

    // Clearing these automatically was tried and is not here, which is worth
    // saying so that nobody spends the afternoon on it twice.
    //
    // EditorApplication.quitting looked right - the editor closing is the one
    // moment nothing is about to draw a word and fill them again, and it is
    // also when somebody is about to commit. It does not work: a plain open
    // and quit of this project, with the hook in, left the atlas exactly as
    // full as it found it. Whatever the callback does, the assets are past
    // saving by the time it runs.
    //
    // OnWillSaveAssets is the other obvious hook and is worse: the atlas
    // refills the instant anything is drawn, so it would be cleared and dirty
    // again within the same second, for ever.
    //
    // So it is a button, and it wants pressing before a commit. One test run
    // put a hundred and eighty kilobytes back into Freeride, so it is worth
    // the press.

    // Beside the localisation and the intro: what the player reads, and the
    // housekeeping that keeps it out of the diff.
    [MenuItem("Tools/Wherever I Am/Clear Font Atlases", false, 202)]
    private static void ClearFromMenu()
    {
        int cleared = Clear();

        EditorUtility.DisplayDialog(
            "Clear Font Atlases",
            cleared == 0
                ? "Every font atlas was already empty."
                : $"Emptied {cleared} font atlas(es). The glyphs come back as " +
                  "soon as anything is drawn with them.",
            "OK");
    }

    /// <summary>
    /// Empties every generated atlas under the project's fonts folder.
    /// Returns how many actually had anything in them.
    /// </summary>
    /// <remarks>
    /// Public and callable from a batch run on purpose: clearing these is the
    /// kind of thing a build machine should be able to do without a person.
    /// </remarks>
    public static int Clear()
    {
        int cleared = 0;

        foreach (FontAsset font in Fonts())
        {
            // Asked first, because clearing an empty one still dirties the
            // asset and would put it back in the diff this exists to keep it
            // out of.
            //
            // The texture is asked about as well as the tables, and that is
            // not belt and braces. Emptying the tables leaves the atlas
            // texture behind - a thousand pixels square of mostly nothing,
            // still a hundred and eighty kilobytes, still different after
            // every run that drew a letter. A font whose tables are empty and
            // whose atlas is not has not been cleared, it has been half
            // cleared, and the half that is left is the half that churns.
            if (font.glyphTable.Count == 0 &&
                font.characterTable.Count == 0 &&
                !HasAtlas(font))
            {
                continue;
            }

            font.ClearFontAssetData(true);
            EditorUtility.SetDirty(font);
            cleared++;
        }

        if (cleared > 0)
            AssetDatabase.SaveAssets();

        return cleared;
    }

    private static bool HasAtlas(FontAsset font)
    {
        if (font.atlasTextures == null)
            return false;

        for (int i = 0; i < font.atlasTextures.Length; i++)
        {
            Texture2D atlas = font.atlasTextures[i];

            if (atlas != null && atlas.width > 1 && atlas.height > 1)
                return true;
        }

        return false;
    }

    private static IEnumerable<FontAsset> Fonts()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:FontAsset", new[] { FontsFolder }))
        {
            FontAsset font =
                AssetDatabase.LoadAssetAtPath<FontAsset>(AssetDatabase.GUIDToAssetPath(guid));

            if (font != null)
                yield return font;
        }
    }
}
