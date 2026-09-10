using UnityEngine;

// What the crosshair looks like, in one asset.
//
// It used to be two facts kept apart and neither of them here. The picture for
// "nothing in reach" was a sprite on the player prefab, the picture for an
// action came off whatever the player was looking at, and the size was
// whichever one the HUD prefab happened to have been drawn at - one number for
// both. So a dot small enough to aim with made every action icon a dot too,
// and an icon big enough to read made the resting crosshair a blob.
//
// A crosshair at rest and a crosshair offering an action are two different
// things that happen to live in the same box, so they get two sizes here and
// the box takes whichever applies.
[CreateAssetMenu(
    fileName = "CrosshairStyle",
    menuName = "Wherever I Am/Player/Crosshair Style")]
public sealed class CrosshairStyle : ScriptableObject
{
    [Header("At rest")]
    [Tooltip("Shown whenever nothing in reach offers an action.")]
    [SerializeField] private Sprite defaultSprite;

    [Tooltip("Height in pixels before the player's own crosshair size is applied.")]
    [SerializeField, Min(1f)] private float defaultHeight = 8f;

    [Header("Offering an action")]
    [Tooltip(
        "Height in pixels for the icon an interactable supplies. The sprite " +
        "itself comes from the object being looked at, not from here.")]
    [SerializeField, Min(1f)] private float actionHeight = 32f;

    public Sprite DefaultSprite => defaultSprite;
    public float DefaultHeight => Mathf.Max(1f, defaultHeight);
    public float ActionHeight => Mathf.Max(1f, actionHeight);

    // The whole answer in one call, because the picture and the size it is
    // drawn at are one decision. Asking for them separately is how they end up
    // disagreeing - an icon at the resting size, or a dot blown up to an icon's.
    //
    // A null sprite is the honest way to say nothing is in reach: an
    // interactable that offers no icon and empty space are the same thing as
    // far as this is concerned, and both get the resting crosshair.
    public CrosshairPose Resolve(Sprite interactionSprite)
    {
        bool isAction = interactionSprite != null;
        Sprite sprite = isAction ? interactionSprite : defaultSprite;
        float height = isAction ? ActionHeight : DefaultHeight;

        return new CrosshairPose(sprite, Measure(sprite, height));
    }

    // Height is what is configured; width follows the sprite. A crosshair is
    // square and says nothing either way, but an action icon is a drawing of
    // something - a hand, a key, an eye - and forcing those into a square is
    // the one thing an artist cannot work around from their end.
    private static Vector2 Measure(Sprite sprite, float height)
    {
        if (sprite == null)
            return new Vector2(height, height);

        Rect rect = sprite.rect;
        float aspect = rect.height > 0f ? rect.width / rect.height : 1f;

        return new Vector2(height * aspect, height);
    }
}

// A picture and the size to draw it at, which travel together or not at all.
public readonly struct CrosshairPose
{
    public readonly Sprite Sprite;
    public readonly Vector2 Size;

    public CrosshairPose(Sprite sprite, Vector2 size)
    {
        Sprite = sprite;
        Size = size;
    }
}
