using UnityEditor;
using UnityEngine;

// Rooms are laid out by looking at the whole floor plan at once, not one volume
// at a time, so these draw whether or not anything is selected. Toggle them off
// in the scene view Gizmos dropdown like any other component gizmo.
public static class RoomVolumeGizmos
{
    private const float SelectedFillAlpha = 0.14f;
    private const float UnselectedFillAlpha = 0.05f;
    private const float SelectedLineAlpha = 0.9f;
    private const float UnselectedLineAlpha = 0.35f;

    [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.Pickable)]
    private static void DrawRoom(RoomVolume room, GizmoType gizmoType)
    {
        if (room == null)
        {
            return;
        }

        room.Refresh();

        if (!room.HasVolume)
        {
            return;
        }

        bool isSelected = (gizmoType & GizmoType.Selected) != 0;
        Color baseColor = GetRoomColor(room.RoomId);

        DrawParts(room, baseColor, isSelected);
        DrawLabel(room, baseColor, isSelected);
    }

    private static void DrawParts(
        RoomVolume room,
        Color baseColor,
        bool isSelected
    )
    {
        Color fill = baseColor;
        fill.a = isSelected ? SelectedFillAlpha : UnselectedFillAlpha;

        Color line = baseColor;
        line.a = isSelected ? SelectedLineAlpha : UnselectedLineAlpha;

        for (int i = 0; i < room.Colliders.Count; i++)
        {
            Collider part = room.Colliders[i];

            if (part == null || !part.enabled)
            {
                continue;
            }

            // Drawn in the part's own space so a rotated room reads correctly;
            // the collider bounds alone would always be axis aligned.
            using (new Handles.DrawingScope(line, GetPartMatrix(part)))
            {
                GetLocalBox(part, out Vector3 center, out Vector3 size);

                Handles.DrawWireCube(center, size);

                Handles.color = fill;
                Handles.CubeHandleCap(
                    0,
                    center,
                    Quaternion.identity,
                    0f,
                    EventType.Repaint
                );
            }
        }
    }

    private static void DrawLabel(
        RoomVolume room,
        Color baseColor,
        bool isSelected
    )
    {
        GUIStyle style = new(EditorStyles.miniBoldLabel)
        {
            normal =
            {
                textColor = isSelected
                    ? Color.white
                    : new Color(baseColor.r, baseColor.g, baseColor.b, 0.85f)
            }
        };

        Handles.Label(
            room.Bounds.center + Vector3.up * (room.Bounds.extents.y + 0.25f),
            room.RoomId,
            style
        );
    }

    private static Matrix4x4 GetPartMatrix(Collider part)
    {
        Transform partTransform = part.transform;

        return Matrix4x4.TRS(
            partTransform.position,
            partTransform.rotation,
            partTransform.lossyScale
        );
    }

    private static void GetLocalBox(
        Collider part,
        out Vector3 center,
        out Vector3 size
    )
    {
        if (part is BoxCollider box)
        {
            center = box.center;
            size = box.size;
            return;
        }

        // Anything that is not a box still gets an outline, just an axis
        // aligned one, which is enough to see it in the floor plan.
        Bounds worldBounds = part.bounds;
        center = part.transform.InverseTransformPoint(worldBounds.center);
        size = worldBounds.size;
    }

    // Stable per name, so a room keeps its colour between sessions and two
    // neighbours are very unlikely to share one.
    private static Color GetRoomColor(string roomId)
    {
        int hash = string.IsNullOrEmpty(roomId)
            ? 0
            : roomId.GetHashCode();
        float hue = Mathf.Abs(hash % 360) / 360f;

        return Color.HSVToRGB(hue, 0.65f, 1f);
    }
}
