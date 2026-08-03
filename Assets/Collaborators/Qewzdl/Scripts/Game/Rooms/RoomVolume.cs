using System;
using System.Collections.Generic;
using UnityEngine;

// An authored room. The shape is whatever its colliders describe, so several
// boxes make an L, a U or a doughnut - nothing here assumes the room is convex,
// only that each individual collider is, which is what ClosestPoint needs.
//
// Lookup is a static registry rather than a scene search, matching how
// DoorInteractableObject publishes its line of sight blockers.
[DisallowMultipleComponent]
public sealed class RoomVolume : MonoBehaviour
{
    private const float ContainmentToleranceSqr = 0.0001f;

    private static readonly List<RoomVolume> RegisteredRooms = new();

    [Header("Identity")]
    [Tooltip("Falls back to the object name when left empty.")]
    [SerializeField] private string roomId;

    [Header("Shape")]
    [Tooltip(
        "Leave empty to collect every collider under this object. Use several " +
        "colliders for a room that is not a single box. Non-convex mesh " +
        "colliders are not supported - build the shape out of primitives.")]
    [SerializeField] private Collider[] volumeColliders = Array.Empty<Collider>();

    // The serialized field is the designer's explicit override; this is what is
    // actually used. Keeping them apart matters: writing the collected children
    // back into the serialized list would silently turn "collect automatically"
    // into "this exact list", and every part added afterwards would be ignored.
    private Collider[] resolvedColliders = Array.Empty<Collider>();

    private bool invalidConfigurationLogged;
    private bool solidColliderLogged;

    public static IReadOnlyList<RoomVolume> All => RegisteredRooms;

    public string RoomId =>
        string.IsNullOrWhiteSpace(roomId) ? name : roomId;

    public Bounds Bounds { get; private set; }
    public bool HasVolume { get; private set; }

    // Summed over the parts rather than taken from Bounds: the box around an L
    // is far bigger than the L, which would make the shapes this component
    // exists for lose every overlap to a smaller-looking square room.
    //
    // Overlapping parts still double count where they cross, which is
    // deliberate - only the ordering between rooms matters, and a seam is
    // small next to a room. Rooms built by clicking out a passage at a time
    // overlap a lot, so that error is the one left to watch.
    public float ShapeVolume { get; private set; }

    public IReadOnlyList<Collider> Colliders => resolvedColliders;

    // Re-reads the shape. Needed whenever the colliders under this object
    // change after OnEnable - the editor tooling leans on it, and so would
    // anything that builds a room at runtime.
    public void Refresh()
    {
        ResolveColliders();
        RebuildBounds();
    }

    private void OnEnable()
    {
        ResolveColliders();
        RebuildBounds();

        if (!RegisteredRooms.Contains(this))
        {
            RegisteredRooms.Add(this);
        }
    }

    private void OnDisable()
    {
        RegisteredRooms.Remove(this);
    }

    // Smallest match wins, so a closet volume placed inside a bedroom resolves
    // to the closet. Without that rule the answer would depend on registration
    // order, which is a quiet source of "works on my machine".
    public static bool TryGetRoomAt(Vector3 point, out RoomVolume room)
    {
        room = null;
        float smallestVolume = float.PositiveInfinity;

        for (int i = 0; i < RegisteredRooms.Count; i++)
        {
            RoomVolume candidate = RegisteredRooms[i];

            if (candidate == null || !candidate.Contains(point))
            {
                continue;
            }

            if (candidate.ShapeVolume >= smallestVolume)
            {
                continue;
            }

            smallestVolume = candidate.ShapeVolume;
            room = candidate;
        }

        return room != null;
    }

    public bool Contains(Vector3 point)
    {
        if (!HasVolume || !Bounds.Contains(point))
        {
            return false;
        }


        for (int i = 0; i < resolvedColliders.Length; i++)
        {
            Collider volumeCollider = resolvedColliders[i];

            if (volumeCollider == null || !volumeCollider.enabled)
            {
                continue;
            }

            // ClosestPoint returns the point itself when it is inside.
            if ((volumeCollider.ClosestPoint(point) - point).sqrMagnitude <=
                ContainmentToleranceSqr)
            {
                return true;
            }
        }

        return false;
    }

    private void ResolveColliders()
    {
        resolvedColliders = HasAnyCollider(volumeColliders)
            ? volumeColliders
            : GetComponentsInChildren<Collider>(true);

        if (!HasAnyCollider(resolvedColliders))
        {
            LogMissingColliders();
            return;
        }

        invalidConfigurationLogged = false;
        WarnAboutSolidColliders();
    }

    // A room is a region, not geometry. A solid collider here is an invisible
    // wall the size of a room, which is a miserable thing to discover after
    // marking up a level.
    private void WarnAboutSolidColliders()
    {
        if (solidColliderLogged)
        {
            return;
        }

        for (int i = 0; i < resolvedColliders.Length; i++)
        {
            Collider volumeCollider = resolvedColliders[i];

            if (volumeCollider == null || volumeCollider.isTrigger)
            {
                continue;
            }

            solidColliderLogged = true;

            Debug.LogWarning(
                $"{nameof(RoomVolume)} '{RoomId}' has a collider that is not " +
                "a trigger, so it will block movement like a wall. Tick Is " +
                "Trigger on every collider describing the room.",
                this
            );

            return;
        }

        solidColliderLogged = false;
    }

    private void RebuildBounds()
    {
        HasVolume = false;
        ShapeVolume = 0f;

        for (int i = 0; i < resolvedColliders.Length; i++)
        {
            Collider volumeCollider = resolvedColliders[i];

            if (volumeCollider == null || !volumeCollider.enabled)
            {
                continue;
            }

            ShapeVolume += MeasurePartVolume(volumeCollider);

            if (!HasVolume)
            {
                Bounds = volumeCollider.bounds;
                HasVolume = true;
                continue;
            }

            Bounds bounds = Bounds;
            bounds.Encapsulate(volumeCollider.bounds);
            Bounds = bounds;
        }
    }

    // A box collider's own size, scaled by its transform, is exact at any
    // angle. Its bounds is the world aligned box drawn around it, which for a
    // room turned 45 degrees reports twice the space the room covers - enough
    // to lose it every overlap it should win.
    private static float MeasurePartVolume(Collider volumeCollider)
    {
        if (volumeCollider is BoxCollider box)
        {
            Vector3 scale = box.transform.lossyScale;
            Vector3 size = new(
                box.size.x * Mathf.Abs(scale.x),
                box.size.y * Mathf.Abs(scale.y),
                box.size.z * Mathf.Abs(scale.z)
            );

            return size.x * size.y * size.z;
        }

        Vector3 bounds = volumeCollider.bounds.size;

        return bounds.x * bounds.y * bounds.z;
    }

    private static bool HasAnyCollider(Collider[] colliders)
    {
        if (colliders == null)
        {
            return false;
        }

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
            {
                return true;
            }
        }

        return false;
    }

    // A room with no shape answers "no" to every query and would otherwise do
    // it in complete silence.
    private void LogMissingColliders()
    {
        if (invalidConfigurationLogged)
        {
            return;
        }

        invalidConfigurationLogged = true;

        Debug.LogError(
            $"{nameof(RoomVolume)} '{RoomId}' has no colliders, so no point " +
            "will ever resolve to it. Add a trigger collider describing the " +
            "room, or several for a shape that is not a single box.",
            this
        );
    }

#if UNITY_EDITOR
    // Drawing lives in RoomVolumeGizmos so it can label rooms and show them all
    // at once, neither of which a runtime OnDrawGizmos can do.
    private void OnValidate()
    {
        Refresh();
    }
#endif
}
