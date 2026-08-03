using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// How the floor and ceiling are decided. The four vertical combinations are
// spelled out because "two checkboxes and a mode switch" reads as three
// independent settings when it is really one choice.
public enum RoomHeightMode
{
    FindFloorAndCeiling,
    FindFloorOnly,
    FindCeilingOnly,
    SetByHand,
    LeaveAsIs,
}

// Which faces the fit is allowed to move, and how the vertical pair is
// decided. Sides left switched off keep the size they already have.
public struct RoomFitOptions
{
    public bool fitMinX;
    public bool fitMaxX;
    public bool fitMinY;
    public bool fitMaxY;
    public bool fitMinZ;
    public bool fitMaxZ;

    // Rooms whose floor and ceiling are a known storey height are faster to
    // type than to search for, and a level with no ceiling geometry has
    // nothing to find.
    public bool useExplicitHeight;
    public float floorY;
    public float ceilingY;

    // What counts as a boundary. Without this the probe stops at the first
    // wardrobe it meets and the room ends up the size of the gap between the
    // furniture.
    public int wallMask;

    // Read the room's angle off the walls instead of making the designer
    // match it by hand. Off leaves the part facing wherever it was put.
    public bool alignToWalls;

    public static RoomFitOptions Default => new()
    {
        fitMinX = true,
        fitMaxX = true,
        fitMinY = true,
        fitMaxY = true,
        fitMinZ = true,
        fitMaxZ = true,
        ceilingY = 3f,
        wallMask = RoomVolumeSetupUtility.DefaultWallMask,
        alignToWalls = true,
    };

    public RoomHeightMode HeightMode
    {
        readonly get
        {
            if (useExplicitHeight)
            {
                return RoomHeightMode.SetByHand;
            }

            if (fitMinY && fitMaxY)
            {
                return RoomHeightMode.FindFloorAndCeiling;
            }

            if (fitMinY)
            {
                return RoomHeightMode.FindFloorOnly;
            }

            return fitMaxY
                ? RoomHeightMode.FindCeilingOnly
                : RoomHeightMode.LeaveAsIs;
        }

        set
        {
            useExplicitHeight = value == RoomHeightMode.SetByHand;
            fitMinY = value is RoomHeightMode.FindFloorAndCeiling
                or RoomHeightMode.FindFloorOnly;
            fitMaxY = value is RoomHeightMode.FindFloorAndCeiling
                or RoomHeightMode.FindCeilingOnly;
        }
    }

    public readonly bool ShouldFit(int axis, int sign)
    {
        bool negative = sign < 0;

        return axis switch
        {
            0 => negative ? fitMinX : fitMaxX,
            1 => negative ? fitMinY : fitMaxY,
            _ => negative ? fitMinZ : fitMaxZ,
        };
    }
}

// Authoring a room by hand means five steps, two of which are easy to forget
// and unpleasant to discover later: a collider left solid is an invisible wall
// the size of a room, and a volume left on the Interactable layer swallows the
// player's interaction ray. Everything here exists so neither can happen by
// accident.
public static class RoomVolumeSetupUtility
{
    public const string VolumeCollidersProperty = "volumeColliders";
    public const string LockedPartsProperty = "lockedParts";
    public const string RoomIdProperty = "roomId";

    public const string RoomLayerName = "Ignore Raycast";
    public const string WallLayerName = "Walls";
    public const string PartNamePrefix = "Part";

    // Only geometry that bounds a room, so props inside it are seen through.
    // Falls back to everything raycastable if the project has no such layer,
    // which is wrong in the same way it was wrong before rather than silently
    // fitting nothing.
    public static int DefaultWallMask
    {
        get
        {
            int wallLayer = LayerMask.NameToLayer(WallLayerName);

            return wallLayer < 0
                ? Physics.DefaultRaycastLayers
                : 1 << wallLayer;
        }
    }

    private const float MaxFitDistance = 30f;
    private const float ProbeThickness = 0.02f;
    private const float ProbeSeedSize = 0.25f;

    private const int AlignmentProbeCount = 64;
    private const int AlignmentBinCount = 18;
    private const float AlignmentWinMargin = 1.5f;
    private const float MaxWallNormalTilt = 0.2f;
    private const float MinimumNormalSqrMagnitude = 0.0001f;
    private const float MinimumAlignmentDegrees = 0.5f;

    // A probe sized exactly to the room grazes the walls it starts between,
    // and a cast that begins in contact reports a zero distance hit. Pulling
    // the face in leaves the width that matters and drops the contact.
    private const float ProbeShrink = 0.9f;

    private static readonly Vector3 DefaultPartSize = new(6f, 3f, 6f);

    // Small enough to land inside a narrow passage without poking through its
    // walls, tall enough that its centre sits above the floor and below the
    // ceiling so both vertical probes have somewhere to go.
    private static readonly Vector3 SeedPartSize = new(1f, 2f, 1f);

    [MenuItem("GameObject/Wherever I Am/Room Volume", false, 11)]
    private static void CreateFromGameObjectMenu(MenuCommand command)
    {
        GameObject parent = command.context as GameObject;
        RoomVolume room = CreateInScene(
            parent != null ? parent.transform : null
        );

        Selection.activeGameObject = room.gameObject;
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    [MenuItem("CONTEXT/RoomVolume/Add Volume Part", false, 100)]
    private static void AddPartFromContextMenu(MenuCommand command)
    {
        if (command.context is not RoomVolume room)
        {
            return;
        }

        Transform part = AddVolumePart(room);
        Selection.activeGameObject = part.gameObject;
    }

    [MenuItem("CONTEXT/RoomVolume/Fit Parts To Walls", false, 101)]
    private static void FitToWallsFromContextMenu(MenuCommand command)
    {
        if (command.context is RoomVolume room)
        {
            FitPartsToWalls(room);
        }
    }

    [MenuItem("CONTEXT/RoomVolume/Fix Colliders And Layer", false, 102)]
    private static void FixFromContextMenu(MenuCommand command)
    {
        if (command.context is RoomVolume room)
        {
            FixCollidersAndLayer(room);
        }
    }

    public static RoomVolume CreateInScene(Transform parent = null)
    {
        GameObject root = new("Room");
        Undo.RegisterCreatedObjectUndo(root, "Create Room Volume");

        if (parent != null)
        {
            Undo.SetTransformParent(root.transform, parent, "Parent Room");
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
        }
        else
        {
            root.transform.position = GetSceneViewPivot();
        }

        ApplyRoomLayer(root);

        // The part comes first on purpose. RoomVolume reports a room with no
        // colliders the moment it wakes, so adding the component to an empty
        // object would greet every created room with an error in the console.
        CreatePart(root.transform, 1);

        return Undo.AddComponent<RoomVolume>(root);
    }

    // Parts are children rather than colliders stacked on the root so each can
    // be moved and scaled on its own - that is what makes an L or a U possible.
    public static Transform AddVolumePart(RoomVolume room)
    {
        if (room == null)
        {
            return null;
        }

        Transform part = CreatePart(
            room.transform,
            CountVolumeParts(room) + 1
        );

        // An empty list means "collect the children", so a new part is picked
        // up on its own and nothing needs writing. A list somebody filled in
        // deliberately is respected instead of wiped - the new part just joins
        // it.
        AppendToExplicitColliderList(room, part.GetComponent<Collider>());

        return part;
    }

    // A room made of passages is placed one passage at a time, and dragging
    // each new part out of the room's origin is the whole job. Given a point
    // on the floor this drops a small seed there and lets the fit expand it
    // into whatever corridor it landed in.
    public static Transform AddVolumePartAt(
        RoomVolume room,
        Vector3 floorPoint,
        RoomFitOptions options
    )
    {
        Transform part = AddVolumePart(room);

        if (part == null)
        {
            return null;
        }

        BoxCollider box = part.GetComponent<BoxCollider>();

        Undo.RecordObject(part, "Place Room Volume Part");
        Undo.RecordObject(box, "Place Room Volume Part");

        part.position = floorPoint + Vector3.up * (SeedPartSize.y * 0.5f);
        box.center = Vector3.zero;
        box.size = Divide(SeedPartSize, SafeScale(part.lossyScale));

        EditorUtility.SetDirty(part);
        EditorUtility.SetDirty(box);

        TryFitPartToWalls(part, options);
        room.Refresh();

        return part;
    }

    private static Transform CreatePart(Transform parent, int index)
    {
        GameObject part = new($"{PartNamePrefix} {index}");
        Undo.RegisterCreatedObjectUndo(part, "Create Room Volume Part");
        Undo.SetTransformParent(
            part.transform,
            parent,
            "Parent Room Volume Part"
        );

        part.transform.localPosition = Vector3.zero;
        part.transform.localRotation = Quaternion.identity;
        part.transform.localScale = Vector3.one;
        ApplyRoomLayer(part);

        BoxCollider partCollider = Undo.AddComponent<BoxCollider>(part);
        partCollider.isTrigger = true;
        partCollider.size = DefaultPartSize;
        partCollider.center = new Vector3(0f, DefaultPartSize.y * 0.5f, 0f);

        return part.transform;
    }

    // The slow part of marking up a level is dragging a box until it lines up
    // with the walls. This grows a part out from its own centre until every
    // enabled face lands on geometry, so the job becomes "drop a part roughly
    // inside the room, press the button". Room volumes are triggers on the
    // raycast ignore layer, so they never fit to each other.
    //
    // Measured along the part's own axes rather than the world's, so a room
    // built at an angle is handled by rotating the part to match it instead of
    // settling for the box drawn around it.
    public static bool TryFitPartToWalls(Transform part, RoomFitOptions options)
    {
        if (part == null)
        {
            return false;
        }

        BoxCollider box = part.GetComponent<BoxCollider>();

        if (box == null)
        {
            return false;
        }

        Physics.SyncTransforms();

        Vector3 centre = part.TransformPoint(box.center);

        if (options.alignToWalls &&
            TryFindWallAngle(centre, options.wallMask, out float wallYaw))
        {
            AlignPartAround(part, centre, wallYaw);
        }

        Quaternion orientation = part.rotation;
        Vector3 scale = SafeScale(part.lossyScale);
        Vector3 currentHalf = Vector3.Scale(box.size * 0.5f, scale);

        // Two passes. The first probes with a small seed, because BoxCast
        // ignores whatever it already starts inside - a part dropped in
        // oversized would otherwise never see the wall it is clipping. The
        // second re-probes with room sized faces, so a doorway cannot leak the
        // volume into the room next door.
        MeasureReaches(
            centre,
            orientation,
            Vector3.one * (ProbeSeedSize * 0.5f),
            currentHalf,
            options,
            out Vector3 negative,
            out Vector3 positive
        );
        MeasureReaches(
            centre,
            orientation,
            Vector3.Min(negative, positive),
            currentHalf,
            options,
            out negative,
            out positive
        );

        if (options.useExplicitHeight && !TryApplyExplicitHeight(
                centre,
                options,
                ref negative,
                ref positive))
        {
            return false;
        }

        Vector3 size = negative + positive;

        if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
        {
            return false;
        }

        Undo.RecordObject(box, "Fit Room Volume Part");
        box.center += Divide(positive - negative, scale) * 0.5f;
        box.size = Divide(size, scale);
        EditorUtility.SetDirty(box);

        return true;
    }

    public static int FitPartsToWalls(RoomVolume room)
    {
        return FitPartsToWalls(room, RoomFitOptions.Default);
    }

    public static int FitPartsToWalls(RoomVolume room, RoomFitOptions options)
    {
        if (room == null)
        {
            return 0;
        }

        Collider[] parts =
            room.GetComponentsInChildren<Collider>(includeInactive: true);
        int fitted = 0;

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == null || room.IsPartLocked(parts[i]))
            {
                continue;
            }

            if (TryFitPartToWalls(parts[i].transform, options))
            {
                fitted++;
            }
        }

        room.Refresh();

        return fitted;
    }

    public static int CountUnlockedParts(RoomVolume room)
    {
        if (room == null)
        {
            return 0;
        }

        Collider[] parts =
            room.GetComponentsInChildren<Collider>(includeInactive: true);
        int unlocked = 0;

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] != null && !room.IsPartLocked(parts[i]))
            {
                unlocked++;
            }
        }

        return unlocked;
    }

    // Through a SerializedObject rather than the field so the change undoes
    // and so a prefab instance records it as an override.
    public static void SetPartLocked(
        RoomVolume room,
        Collider part,
        bool locked
    )
    {
        if (room == null || part == null)
        {
            return;
        }

        SerializedObject serialized = new(room);
        SerializedProperty lockedParts =
            serialized.FindProperty(LockedPartsProperty);

        if (lockedParts == null)
        {
            return;
        }

        int existing = IndexOfPart(lockedParts, part);

        if (locked && existing < 0)
        {
            lockedParts.arraySize++;
            lockedParts
                .GetArrayElementAtIndex(lockedParts.arraySize - 1)
                .objectReferenceValue = part;
        }
        else if (!locked && existing >= 0)
        {
            // Assigning null first: on an object reference array, deleting the
            // element only clears it the first time round.
            lockedParts.GetArrayElementAtIndex(existing).objectReferenceValue =
                null;
            lockedParts.DeleteArrayElementAtIndex(existing);
        }
        else
        {
            return;
        }

        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(room);
        PrefabUtility.RecordPrefabInstancePropertyModifications(room);
    }

    private static int IndexOfPart(SerializedProperty array, Collider part)
    {
        for (int i = 0; i < array.arraySize; i++)
        {
            if (array.GetArrayElementAtIndex(i).objectReferenceValue == part)
            {
                return i;
            }
        }

        return -1;
    }

    // The walls already tell us which way the room faces - every cast returns
    // the normal of what it hit. Reading it off beats making the designer
    // match the angle by hand and then discovering it was two degrees out.
    public static bool TryFindWallAngle(Vector3 centre, int wallMask, out float yaw)
    {
        yaw = 0f;

        // Opposite walls of a rectangular room have normals 180 degrees apart
        // and adjacent ones 90, so every wall describes the same angle once
        // folded into a quarter turn.
        //
        // What is done with those angles is the whole difficulty. An average
        // is dragged by everything that disagrees - a diagonal alcove, a crate
        // sitting askew, a wall seen through a doorway - and lands on an angle
        // no wall in the room actually has. So: bin them, take the busiest
        // bin, and average only inside it. Whatever disagrees is not
        // outweighed, it is discarded.
        float[] bins = new float[AlignmentBinCount];
        List<float> foldedAngles = new();

        for (int i = 0; i < AlignmentProbeCount; i++)
        {
            Vector3 direction = Quaternion.Euler(
                0f,
                i * (360f / AlignmentProbeCount),
                0f
            ) * Vector3.forward;

            if (!Physics.Raycast(
                    centre,
                    direction,
                    out RaycastHit hit,
                    MaxFitDistance,
                    wallMask,
                    QueryTriggerInteraction.Ignore))
            {
                continue;
            }

            Vector3 normal = hit.normal;

            // Floors and ceilings say nothing about which way a room faces.
            if (Mathf.Abs(normal.y) > MaxWallNormalTilt)
            {
                continue;
            }

            normal.y = 0f;

            if (normal.sqrMagnitude < MinimumNormalSqrMagnitude)
            {
                continue;
            }

            float folded = Mathf.Repeat(
                Mathf.Atan2(normal.x, normal.z) * Mathf.Rad2Deg,
                90f
            );

            foldedAngles.Add(folded);
            bins[BinOf(folded)] += 1f;
        }

        if (foldedAngles.Count == 0)
        {
            return false;
        }

        int bestBin = LargestBin(bins, -1);
        int rivalBin = LargestBin(bins, bestBin);

        // A shape with no rectangle in it - an octagon has walls every 45
        // degrees, and two bins tie forever. Whichever won would be a coin
        // toss that moves the room every time the button is pressed, so
        // refuse and leave the part as the designer placed it.
        if (bins[bestBin] < bins[rivalBin] * AlignmentWinMargin)
        {
            return false;
        }

        yaw = RefineAngleInBin(foldedAngles, bestBin);

        return true;
    }

    private static int BinOf(float foldedAngle)
    {
        return Mathf.Clamp(
            Mathf.FloorToInt(foldedAngle * AlignmentBinCount / 90f),
            0,
            AlignmentBinCount - 1
        );
    }

    // Bin 0 and the last bin are neighbours: 1 degree and 89 are two degrees
    // apart, not eighty-eight.
    private static int BinDistance(int from, int to)
    {
        int direct = Mathf.Abs(from - to);

        return Mathf.Min(direct, AlignmentBinCount - direct);
    }

    // Passing a bin to avoid finds the runner up, which has to be a genuinely
    // different angle rather than the same peak spilling into the bin next
    // door.
    private static int LargestBin(float[] bins, int avoid)
    {
        int largest = 0;
        float largestCount = -1f;

        for (int bin = 0; bin < bins.Length; bin++)
        {
            if (avoid >= 0 && BinDistance(bin, avoid) <= 1)
            {
                continue;
            }

            if (bins[bin] <= largestCount)
            {
                continue;
            }

            largestCount = bins[bin];
            largest = bin;
        }

        return largest;
    }

    // Bins are five degrees wide, which is coarser than the answer needs to
    // be. Averaging the members of the winning bin and its neighbours puts the
    // angle back to full precision without letting anything outside it vote.
    private static float RefineAngleInBin(List<float> foldedAngles, int bestBin)
    {
        Vector2 sum = Vector2.zero;

        for (int i = 0; i < foldedAngles.Count; i++)
        {
            if (BinDistance(BinOf(foldedAngles[i]), bestBin) > 1)
            {
                continue;
            }

            // Round the circle four times, so 89 degrees and 1 degree average
            // to 90 rather than to 45.
            float quadrupled = foldedAngles[i] * 4f * Mathf.Deg2Rad;

            sum += new Vector2(Mathf.Cos(quadrupled), Mathf.Sin(quadrupled));
        }

        if (sum.sqrMagnitude < MinimumNormalSqrMagnitude)
        {
            return (bestBin + 0.5f) * (90f / AlignmentBinCount);
        }

        return Mathf.Repeat(
            Mathf.Atan2(sum.y, sum.x) * Mathf.Rad2Deg * 0.25f,
            90f
        );
    }

    // A rectangle looks the same every quarter turn, so the walls can only
    // ever pin the angle down to one of four. Picking the one nearest to how
    // the part already sits stops a part the designer turned to 120 degrees
    // from snapping to 30 - the same angle, but the box visibly spins and the
    // sides swap which wall they belong to.
    private static float NearestQuarterTurn(float currentYaw, float yaw)
    {
        float nearest = yaw;
        float nearestDelta = Mathf.Abs(Mathf.DeltaAngle(currentYaw, yaw));

        for (int quarter = 1; quarter < 4; quarter++)
        {
            float candidate = yaw + quarter * 90f;
            float delta = Mathf.Abs(Mathf.DeltaAngle(currentYaw, candidate));

            if (delta >= nearestDelta)
            {
                continue;
            }

            nearestDelta = delta;
            nearest = candidate;
        }

        return nearest;
    }

    // Turning the part spins its box around the part's origin, so the centre
    // the probes were about to fire from would move. Put it back.
    private static void AlignPartAround(Transform part, Vector3 centre, float yaw)
    {
        yaw = NearestQuarterTurn(part.eulerAngles.y, yaw);

        if (Mathf.Abs(Mathf.DeltaAngle(part.eulerAngles.y, yaw)) <
            MinimumAlignmentDegrees)
        {
            return;
        }

        Undo.RecordObject(part, "Align Room Volume Part");

        part.rotation = Quaternion.Euler(0f, yaw, 0f);

        BoxCollider box = part.GetComponent<BoxCollider>();
        part.position += centre - part.TransformPoint(box.center);

        EditorUtility.SetDirty(part);
    }

    // Floor and ceiling are world planes, so this means what it says only
    // while the part is level. Yaw is fine, which is the case that matters -
    // rooms are built at an angle, not on a slope.
    private static bool TryApplyExplicitHeight(
        Vector3 centre,
        RoomFitOptions options,
        ref Vector3 negative,
        ref Vector3 positive
    )
    {
        float below = centre.y - options.floorY;
        float above = options.ceilingY - centre.y;

        if (below <= 0f || above <= 0f)
        {
            return false;
        }

        negative.y = below;
        positive.y = above;

        return true;
    }

    private static void MeasureReaches(
        Vector3 centre,
        Quaternion orientation,
        Vector3 probeHalf,
        Vector3 currentHalf,
        RoomFitOptions options,
        out Vector3 negative,
        out Vector3 positive
    )
    {
        // A face that is not being fitted keeps the size it has, which is what
        // makes a room with one open side workable: switch that side off and
        // place it by hand, let the other five find themselves.
        negative = currentHalf;
        positive = currentHalf;

        for (int axis = 0; axis < 3; axis++)
        {
            for (int sign = -1; sign <= 1; sign += 2)
            {
                if (!options.ShouldFit(axis, sign))
                {
                    continue;
                }

                float measured = MeasureReach(
                    centre,
                    orientation * AxisVector(axis) * sign,
                    orientation,
                    BuildProbe(probeHalf, axis),
                    currentHalf[axis],
                    options.wallMask
                );

                if (sign < 0)
                {
                    negative[axis] = measured;
                }
                else
                {
                    positive[axis] = measured;
                }
            }
        }
    }

    // A slab facing the cast direction: thin along it, as wide as the room is
    // believed to be across it. The width is what stops a doorway sized gap
    // from swallowing the probe.
    private static Vector3 BuildProbe(Vector3 probeHalf, int axis)
    {
        Vector3 halfExtents = probeHalf;

        for (int i = 0; i < 3; i++)
        {
            halfExtents[i] = i == axis
                ? ProbeThickness
                : Mathf.Max(halfExtents[i] * ProbeShrink, ProbeThickness);
        }

        return halfExtents;
    }

    private static float MeasureReach(
        Vector3 centre,
        Vector3 direction,
        Quaternion orientation,
        Vector3 halfExtents,
        float fallbackReach,
        int wallMask
    )
    {
        bool hitSomething = Physics.BoxCast(
            centre,
            halfExtents,
            direction,
            out RaycastHit hit,
            orientation,
            MaxFitDistance,
            wallMask,
            QueryTriggerInteraction.Ignore
        );

        // Nothing that way - an open edge, or a level that simply stops. Keep
        // the face the designer already placed instead of growing to the
        // horizon.
        return hitSomething
            ? hit.distance + ProbeThickness
            : Mathf.Abs(fallbackReach);
    }

    private static Vector3 AxisVector(int axis)
    {
        return axis switch
        {
            0 => Vector3.right,
            1 => Vector3.up,
            _ => Vector3.forward,
        };
    }

    private static Vector3 SafeScale(Vector3 scale)
    {
        return new Vector3(
            Mathf.Approximately(scale.x, 0f) ? 1f : Mathf.Abs(scale.x),
            Mathf.Approximately(scale.y, 0f) ? 1f : Mathf.Abs(scale.y),
            Mathf.Approximately(scale.z, 0f) ? 1f : Mathf.Abs(scale.z)
        );
    }

    private static Vector3 Divide(Vector3 value, Vector3 divisor)
    {
        return new Vector3(
            value.x / divisor.x,
            value.y / divisor.y,
            value.z / divisor.z
        );
    }

    public static void FixCollidersAndLayer(RoomVolume room)
    {
        if (room == null)
        {
            return;
        }

        Collider[] colliders =
            room.GetComponentsInChildren<Collider>(includeInactive: true);

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider roomCollider = colliders[i];

            if (roomCollider == null)
            {
                continue;
            }

            if (!roomCollider.isTrigger)
            {
                Undo.RecordObject(roomCollider, "Fix Room Volume Collider");
                roomCollider.isTrigger = true;
                EditorUtility.SetDirty(roomCollider);
            }

            ApplyRoomLayer(roomCollider.gameObject);
        }

        ApplyRoomLayer(room.gameObject);
    }

    public static IReadOnlyList<string> GetSetupProblems(RoomVolume room)
    {
        List<string> problems = new();

        if (room == null)
        {
            problems.Add("The room volume component is missing.");
            return problems;
        }

        Collider[] colliders =
            room.GetComponentsInChildren<Collider>(includeInactive: true);

        if (colliders.Length == 0)
        {
            problems.Add(
                "The room has no colliders, so no point resolves to it."
            );
            return problems;
        }

        int solidCount = 0;
        int wrongLayerCount = 0;
        int roomLayer = LayerMask.NameToLayer(RoomLayerName);

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider roomCollider = colliders[i];

            if (roomCollider == null)
            {
                continue;
            }

            if (!roomCollider.isTrigger)
            {
                solidCount++;
            }

            if (roomLayer >= 0 && roomCollider.gameObject.layer != roomLayer)
            {
                wrongLayerCount++;
            }
        }

        if (solidCount > 0)
        {
            problems.Add(
                $"{solidCount} collider(s) are not triggers and will block " +
                "movement like a wall."
            );
        }

        if (wrongLayerCount > 0)
        {
            problems.Add(
                $"{wrongLayerCount} collider(s) are not on the " +
                $"{RoomLayerName} layer, where they can be picked up by " +
                "gameplay raycasts."
            );
        }

        return problems;
    }

    public static bool HasCompleteSetup(RoomVolume room)
    {
        return GetSetupProblems(room).Count == 0;
    }

    public static int CountVolumeParts(RoomVolume room)
    {
        return room == null
            ? 0
            : room.GetComponentsInChildren<Collider>(includeInactive: true)
                .Length;
    }

    // Fits an axis aligned box, so the part's rotation is reset rather than
    // quietly producing a volume that does not match the bounds it was given.
    public static void FitPartToWorldBounds(Transform part, Bounds bounds)
    {
        if (part == null)
        {
            return;
        }

        BoxCollider box = part.GetComponent<BoxCollider>();

        if (box == null)
        {
            return;
        }

        Undo.RecordObject(part, "Fit Room Volume Part");
        part.position = bounds.center;
        part.rotation = Quaternion.identity;

        Vector3 scale = part.lossyScale;
        Vector3 size = new(
            SafeDivide(bounds.size.x, scale.x),
            SafeDivide(bounds.size.y, scale.y),
            SafeDivide(bounds.size.z, scale.z)
        );

        Undo.RecordObject(box, "Fit Room Volume Part");
        box.center = Vector3.zero;
        box.size = size;

        EditorUtility.SetDirty(part);
        EditorUtility.SetDirty(box);
    }

    public static bool TryGetSelectionBounds(out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;

        GameObject[] selection = Selection.gameObjects;

        for (int i = 0; i < selection.Length; i++)
        {
            GameObject selected = selection[i];

            if (selected == null ||
                selected.GetComponentInParent<RoomVolume>() != null)
            {
                continue;
            }

            Encapsulate(
                selected.GetComponentsInChildren<Renderer>(true),
                ref bounds,
                ref hasBounds
            );
            Encapsulate(
                selected.GetComponentsInChildren<Collider>(true),
                ref bounds,
                ref hasBounds
            );
        }

        return hasBounds;
    }

    private static void Encapsulate(
        Renderer[] renderers,
        ref Bounds bounds,
        ref bool hasBounds
    )
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                Encapsulate(renderers[i].bounds, ref bounds, ref hasBounds);
            }
        }
    }

    private static void Encapsulate(
        Collider[] colliders,
        ref Bounds bounds,
        ref bool hasBounds
    )
    {
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
            {
                Encapsulate(colliders[i].bounds, ref bounds, ref hasBounds);
            }
        }
    }

    private static void Encapsulate(
        Bounds source,
        ref Bounds bounds,
        ref bool hasBounds
    )
    {
        if (!hasBounds)
        {
            bounds = source;
            hasBounds = true;
            return;
        }

        bounds.Encapsulate(source);
    }

    private static float SafeDivide(float value, float divisor)
    {
        return Mathf.Approximately(divisor, 0f)
            ? value
            : value / divisor;
    }

    private static void AppendToExplicitColliderList(
        RoomVolume room,
        Collider part
    )
    {
        if (part == null)
        {
            return;
        }

        SerializedObject serialized = new(room);
        SerializedProperty colliders =
            serialized.FindProperty(VolumeCollidersProperty);

        if (colliders == null || colliders.arraySize == 0)
        {
            return;
        }

        colliders.arraySize++;
        colliders
            .GetArrayElementAtIndex(colliders.arraySize - 1)
            .objectReferenceValue = part;

        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(room);
        PrefabUtility.RecordPrefabInstancePropertyModifications(room);
    }

    private static void ApplyRoomLayer(GameObject target)
    {
        int roomLayer = LayerMask.NameToLayer(RoomLayerName);

        if (roomLayer < 0 || target.layer == roomLayer)
        {
            return;
        }

        Undo.RecordObject(target, "Set Room Volume Layer");
        target.layer = roomLayer;
        EditorUtility.SetDirty(target);
    }

    private static Vector3 GetSceneViewPivot()
    {
        SceneView view = SceneView.lastActiveSceneView;
        return view != null ? view.pivot : Vector3.zero;
    }
}
