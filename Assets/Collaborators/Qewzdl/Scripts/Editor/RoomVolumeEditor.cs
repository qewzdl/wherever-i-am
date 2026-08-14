using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(RoomVolume))]
public sealed class RoomVolumeEditor : Editor
{
    private const string EditInScenePreference = "WIAM.RoomVolume.EditInScene";
    private const string PickSidesPreference = "WIAM.RoomVolume.PickSides";
    private const string FitPreferencePrefix = "WIAM.RoomVolume.Fit.";

    private static readonly Color FittedFaceColor = new(0.35f, 0.85f, 0.45f);
    private static readonly Color LockedFaceColor = new(0.65f, 0.65f, 0.65f);
    private static readonly Color TypedFaceColor = new(0.35f, 0.7f, 0.95f);

    private static readonly string[] HeightModeLabels =
    {
        "Find floor and ceiling",
        "Find floor, keep ceiling",
        "Find ceiling, keep floor",
        "Type both by hand",
        "Keep both as they are",
    };

    private const float MaxPlacementDistance = 1000f;

    private readonly List<BoxBoundsHandle> partHandles = new();

    // Transient on purpose: changing selection ends placement, which is what
    // clicking away means anyway.
    private bool placingParts;

    private SerializedProperty roomId;
    private SerializedProperty volumeColliders;

    // Settings are per room. They used to be one global set, which meant a
    // room with an open side quietly handed its exception to the next room
    // the designer opened.
    private string fitKeyPrefix;

    private RoomVolume Room => (RoomVolume)target;

    private static bool EditInScene
    {
        get => EditorPrefs.GetBool(EditInScenePreference, true);
        set => EditorPrefs.SetBool(EditInScenePreference, value);
    }

    // Off by default. Every side grows unless it is told not to, so markers on
    // all six of them draw the ordinary case and nothing else - clutter around
    // every room in the level to say that nothing unusual is set. The
    // inspector already lists which sides are locked.
    private static bool PickSidesInScene
    {
        get => EditorPrefs.GetBool(PickSidesPreference, false);
        set => EditorPrefs.SetBool(PickSidesPreference, value);
    }

    private void OnEnable()
    {
        roomId = serializedObject.FindProperty(
            RoomVolumeSetupUtility.RoomIdProperty
        );
        volumeColliders = serializedObject.FindProperty(
            RoomVolumeSetupUtility.VolumeCollidersProperty
        );

        // Slow enough to be worth doing once per selection, not once per
        // repaint.
        fitKeyPrefix =
            $"{FitPreferencePrefix}{GlobalObjectId.GetGlobalObjectIdSlow(Room)}.";
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawIdentity();
        DrawSetupStatus();
        DrawFitPanel();
        DrawPartActions();
        DrawShape();

        serializedObject.ApplyModifiedProperties();

        // After the apply: locking goes through its own SerializedObject, and
        // running both over the same component in one pass loses whichever
        // wrote first.
        DrawPartLocks();
    }

    private void DrawIdentity()
    {
        EditorGUILayout.PropertyField(roomId);

        if (string.IsNullOrWhiteSpace(roomId.stringValue))
        {
            EditorGUILayout.LabelField(
                " ",
                $"Falls back to the object name: {Room.name}",
                EditorStyles.miniLabel
            );
        }
    }

    private void DrawSetupStatus()
    {
        EditorGUILayout.Space(6f);

        IReadOnlyList<string> problems =
            RoomVolumeSetupUtility.GetSetupProblems(Room);

        if (problems.Count > 0)
        {
            EditorGUILayout.HelpBox(
                string.Join("\n", problems),
                MessageType.Warning
            );

            return;
        }

        EditorGUILayout.LabelField(
            $"{RoomVolumeSetupUtility.CountVolumeParts(Room)} part(s), " +
            $"{Room.ShapeVolume:0.#} m³.",
            EditorStyles.miniLabel
        );
    }

    // The button and the settings that decide what it does live in one box,
    // with the settings above the button rather than folded away below it.
    private void DrawFitPanel()
    {
        EditorGUILayout.Space(6f);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            RoomFitOptions options = LoadOptions();

            EditorGUI.BeginChangeCheck();

            options.wallMask = DrawLayerMaskField(
                "Walls are on", options.wallMask);

            options = DrawSides(options);
            options = DrawHeight(options);

            if (EditorGUI.EndChangeCheck())
            {
                StoreOptions(options);
            }

            EditorGUILayout.LabelField(
                DescribeFit(options),
                EditorStyles.miniLabel
            );

            EditorGUILayout.Space(2f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Fit Parts To Walls", GUILayout.Height(24f)))
                {
                    RunFit(options);
                }

                if (GUILayout.Button(
                        "Reset",
                        GUILayout.Height(24f),
                        GUILayout.Width(60f)))
                {
                    StoreOptions(RoomFitOptions.Default);
                }
            }
        }
    }

    private RoomFitOptions DrawSides(RoomFitOptions options)
    {
        options.alignToWalls = EditorGUILayout.Toggle(
            "Find the angle", options.alignToWalls);

        EditorGUILayout.LabelField("Sides", EditorStyles.miniBoldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            options.fitMinX = DrawSideToggle(options.fitMinX, "-X");
            options.fitMaxX = DrawSideToggle(options.fitMaxX, "+X");
            options.fitMinZ = DrawSideToggle(options.fitMinZ, "-Z");
            options.fitMaxZ = DrawSideToggle(options.fitMaxZ, "+Z");
        }

        return options;
    }

    private static bool DrawSideToggle(bool value, string label)
    {
        return GUILayout.Toggle(value, label, EditorStyles.miniButton);
    }

    private RoomFitOptions DrawHeight(RoomFitOptions options)
    {
        EditorGUILayout.Space(2f);

        options.HeightMode = (RoomHeightMode)EditorGUILayout.Popup(
            "Height",
            (int)options.HeightMode,
            HeightModeLabels
        );

        if (options.HeightMode != RoomHeightMode.SetByHand)
        {
            return options;
        }

        EditorGUI.indentLevel++;
        options.floorY = EditorGUILayout.FloatField("Floor Y", options.floorY);
        options.ceilingY = EditorGUILayout.FloatField(
            "Ceiling Y", options.ceilingY);
        EditorGUI.indentLevel--;

        return options;
    }

    // One line saying what pressing the button will do, so the settings do not
    // have to be read back and assembled in the designer's head.
    private static string DescribeFit(RoomFitOptions options)
    {
        int sides = 0;

        if (options.fitMinX) sides++;
        if (options.fitMaxX) sides++;
        if (options.fitMinZ) sides++;
        if (options.fitMaxZ) sides++;

        string height = options.HeightMode switch
        {
            RoomHeightMode.FindFloorAndCeiling => "floor and ceiling found",
            RoomHeightMode.FindFloorOnly => "floor found",
            RoomHeightMode.FindCeilingOnly => "ceiling found",
            RoomHeightMode.SetByHand =>
                $"height {options.floorY:0.##} to {options.ceilingY:0.##}",
            _ => "height untouched",
        };

        string layers = DescribeMask(options.wallMask);

        string angle = options.alignToWalls
            ? "Angle read off the walls"
            : "Angle left as placed";

        return sides == 0
            ? $"No side will move. {height}. {angle}."
            : $"{sides} of 4 sides grow to {layers}. {height}. {angle}.";
    }

    private static string DescribeMask(int mask)
    {
        List<string> names = new();

        for (int layer = 0; layer < 32; layer++)
        {
            if ((mask & (1 << layer)) == 0)
            {
                continue;
            }

            string name = LayerMask.LayerToName(layer);

            if (!string.IsNullOrEmpty(name))
            {
                names.Add(name);
            }
        }

        return names.Count switch
        {
            0 => "nothing",
            > 3 => $"{names.Count} layers",
            _ => string.Join(", ", names),
        };
    }

    private void RunFit(RoomFitOptions options)
    {
        int fitted = RoomVolumeSetupUtility.FitPartsToWalls(Room, options);

        if (fitted > 0)
        {
            return;
        }

        if (RoomVolumeSetupUtility.CountUnlockedParts(Room) == 0)
        {
            Debug.LogWarning(
                "Every part of this room is locked, so the button had " +
                "nothing to do.",
                Room
            );

            return;
        }

        Debug.LogWarning(
            "No part could be fitted. Move the part inside the room - it " +
            "grows outwards from its own centre - and check that the floor " +
            "is below its centre and the ceiling above it.",
            Room
        );
    }

    private void DrawPartActions()
    {
        EditorGUILayout.Space(4f);

        bool nowPlacing = GUILayout.Toggle(
            placingParts,
            placingParts
                ? "Click a passage to add a part  (Esc to stop)"
                : "Place Parts By Clicking",
            EditorStyles.miniButton,
            GUILayout.Height(22f)
        );

        if (nowPlacing != placingParts)
        {
            placingParts = nowPlacing;
            SceneView.RepaintAll();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Part"))
            {
                RoomVolumeSetupUtility.AddVolumePart(Room);
            }

            using (new EditorGUI.DisabledScope(
                       !RoomVolumeSetupUtility.TryGetSelectionBounds(out _)))
            {
                if (GUILayout.Button("Add Part Fitting Selection"))
                {
                    AddPartFittingSelection();
                }
            }

            using (new EditorGUI.DisabledScope(
                       RoomVolumeSetupUtility.HasCompleteSetup(Room)))
            {
                if (GUILayout.Button("Fix Colliders And Layer"))
                {
                    RoomVolumeSetupUtility.FixCollidersAndLayer(Room);
                }
            }
        }

        if (GUILayout.Button("Drop Parts To Floor"))
        {
            RoomVolumeSetupUtility.DropPartsToFloor(Room);
        }

        EditInScene = EditorGUILayout.ToggleLeft(
            "Resize parts in the scene view",
            EditInScene
        );

        bool nowPicking = EditorGUILayout.ToggleLeft(
            "Pick sides in the scene view",
            PickSidesInScene
        );

        if (nowPicking != PickSidesInScene)
        {
            PickSidesInScene = nowPicking;
            SceneView.RepaintAll();
        }
    }

    private RoomFitOptions LoadOptions()
    {
        RoomFitOptions fallback = RoomFitOptions.Default;

        return new RoomFitOptions
        {
            fitMinX = GetFlag("MinX", fallback.fitMinX),
            fitMaxX = GetFlag("MaxX", fallback.fitMaxX),
            fitMinY = GetFlag("MinY", fallback.fitMinY),
            fitMaxY = GetFlag("MaxY", fallback.fitMaxY),
            fitMinZ = GetFlag("MinZ", fallback.fitMinZ),
            fitMaxZ = GetFlag("MaxZ", fallback.fitMaxZ),
            useExplicitHeight = GetFlag(
                "ExplicitHeight", fallback.useExplicitHeight),
            alignToWalls = GetFlag("AlignToWalls", fallback.alignToWalls),
            floorY = EditorPrefs.GetFloat(
                fitKeyPrefix + "FloorY", fallback.floorY),
            ceilingY = EditorPrefs.GetFloat(
                fitKeyPrefix + "CeilingY", fallback.ceilingY),
            wallMask = EditorPrefs.GetInt(
                fitKeyPrefix + "WallMask", fallback.wallMask),
        };
    }

    private void StoreOptions(RoomFitOptions options)
    {
        SetFlag("MinX", options.fitMinX);
        SetFlag("MaxX", options.fitMaxX);
        SetFlag("MinY", options.fitMinY);
        SetFlag("MaxY", options.fitMaxY);
        SetFlag("MinZ", options.fitMinZ);
        SetFlag("MaxZ", options.fitMaxZ);
        SetFlag("ExplicitHeight", options.useExplicitHeight);
        SetFlag("AlignToWalls", options.alignToWalls);
        EditorPrefs.SetFloat(fitKeyPrefix + "FloorY", options.floorY);
        EditorPrefs.SetFloat(fitKeyPrefix + "CeilingY", options.ceilingY);
        EditorPrefs.SetInt(fitKeyPrefix + "WallMask", options.wallMask);
    }

    private bool GetFlag(string key, bool fallback)
    {
        return EditorPrefs.GetBool(fitKeyPrefix + key, fallback);
    }

    private void SetFlag(string key, bool value)
    {
        EditorPrefs.SetBool(fitKeyPrefix + key, value);
    }

    // The engine's own conversion: MaskField numbers the named layers in
    // order, which is not the same as their bit positions.
    private static int DrawLayerMaskField(string label, int mask)
    {
        int named = EditorGUILayout.MaskField(
            label,
            InternalEditorUtility.LayerMaskToConcatenatedLayersMask(mask),
            InternalEditorUtility.layers
        );

        return InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(named);
    }

    private void AddPartFittingSelection()
    {
        if (!RoomVolumeSetupUtility.TryGetSelectionBounds(out Bounds bounds))
        {
            return;
        }

        Transform part = RoomVolumeSetupUtility.AddVolumePart(Room);
        RoomVolumeSetupUtility.FitPartToWorldBounds(part, bounds);
    }

    private void OnSceneGUI()
    {
        if (Room == null)
        {
            return;
        }

        Room.Refresh();

        // Placement owns the mouse while it is on: face buttons and resize
        // handles would eat the click meant for the floor.
        if (placingParts)
        {
            HandlePlacement();
            return;
        }

        if (PickSidesInScene)
        {
            DrawFaceToggles();
        }

        if (!EditInScene)
        {
            return;
        }

        int handleIndex = 0;

        for (int i = 0; i < Room.Colliders.Count; i++)
        {
            if (Room.Colliders[i] is not BoxCollider box)
            {
                continue;
            }

            DrawPartHandle(box, handleIndex);
            handleIndex++;
        }
    }

    private void HandlePlacement()
    {
        // Without this the scene view treats the click as "select whatever is
        // under the cursor" and the room is deselected mid placement.
        HandleUtility.AddDefaultControl(
            GUIUtility.GetControlID(FocusType.Passive));

        Event current = Event.current;
        RoomFitOptions options = LoadOptions();
        bool overFloor = TryGetPointUnderMouse(options.wallMask, out Vector3 point);

        if (overFloor)
        {
            Handles.color = FittedFaceColor;
            Handles.DrawWireDisc(
                point,
                Vector3.up,
                HandleUtility.GetHandleSize(point) * 0.35f
            );
            Handles.Label(point, "add a part here");
        }

        switch (current.type)
        {
            case EventType.MouseMove:
                SceneView.RepaintAll();
                return;

            case EventType.KeyDown when current.keyCode == KeyCode.Escape:
                placingParts = false;
                current.Use();
                Repaint();
                return;

            case EventType.MouseDown
                when current.button == 0 && !current.alt && overFloor:
                RoomVolumeSetupUtility.AddVolumePartAt(Room, point, options);
                current.Use();
                Repaint();
                return;
        }
    }

    private static bool TryGetPointUnderMouse(int mask, out Vector3 point)
    {
        point = default;

        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);

        if (!Physics.Raycast(
                ray,
                out RaycastHit hit,
                MaxPlacementDistance,
                mask,
                QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        point = hit.point;

        return true;
    }

    // "-X" means nothing while you are looking at a room. A button floating
    // off each wall, green for grow and grey for locked, means you pick the
    // wall you can see instead of working out which axis it is on.
    private void DrawFaceToggles()
    {
        BoxCollider box = LargestBoxPart();

        if (box == null)
        {
            return;
        }

        RoomFitOptions options = LoadOptions();
        Transform part = box.transform;
        bool changed = false;

        for (int axis = 0; axis < 3; axis++)
        {
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector3 localAxis = AxisVector(axis) * sign;
                Vector3 localFace =
                    box.center + Vector3.Scale(localAxis, box.size * 0.5f);
                Vector3 world = part.TransformPoint(localFace);
                Vector3 outward = part.TransformDirection(localAxis).normalized;

                float handleSize = HandleUtility.GetHandleSize(world) * 0.07f;
                Vector3 buttonPosition = world + outward * (handleSize * 2.5f);

                Handles.color = FaceColor(options, axis, sign);

                if (Handles.Button(
                        buttonPosition,
                        Quaternion.LookRotation(outward),
                        handleSize,
                        handleSize,
                        Handles.DotHandleCap))
                {
                    options = ToggleFace(options, axis, sign);
                    changed = true;
                }

                if (!options.ShouldFit(axis, sign) &&
                    !(axis == 1 && options.useExplicitHeight))
                {
                    Handles.Label(
                        buttonPosition + Vector3.up * handleSize * 1.5f,
                        "locked"
                    );
                }
            }
        }

        if (!changed)
        {
            return;
        }

        StoreOptions(options);
        Repaint();
    }

    private static Color FaceColor(RoomFitOptions options, int axis, int sign)
    {
        if (axis == 1 && options.useExplicitHeight)
        {
            return TypedFaceColor;
        }

        return options.ShouldFit(axis, sign)
            ? FittedFaceColor
            : LockedFaceColor;
    }

    // Clicking a floor or ceiling means "find this one", so it also drops the
    // typed height rather than doing nothing visible.
    private static RoomFitOptions ToggleFace(
        RoomFitOptions options,
        int axis,
        int sign
    )
    {
        bool wanted = !options.ShouldFit(axis, sign);

        if (axis == 1 && options.useExplicitHeight)
        {
            options.useExplicitHeight = false;
            wanted = true;
        }

        switch (axis)
        {
            case 0 when sign < 0: options.fitMinX = wanted; break;
            case 0: options.fitMaxX = wanted; break;
            case 1 when sign < 0: options.fitMinY = wanted; break;
            case 1: options.fitMaxY = wanted; break;
            case 2 when sign < 0: options.fitMinZ = wanted; break;
            default: options.fitMaxZ = wanted; break;
        }

        return options;
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

    private BoxCollider LargestBoxPart()
    {
        BoxCollider largest = null;
        float largestVolume = 0f;

        for (int i = 0; i < Room.Colliders.Count; i++)
        {
            if (Room.Colliders[i] is not BoxCollider box)
            {
                continue;
            }

            Vector3 size = box.bounds.size;
            float volume = size.x * size.y * size.z;

            if (volume <= largestVolume)
            {
                continue;
            }

            largestVolume = volume;
            largest = box;
        }

        return largest;
    }

    private void DrawPartHandle(BoxCollider box, int handleIndex)
    {
        while (partHandles.Count <= handleIndex)
        {
            partHandles.Add(new BoxBoundsHandle());
        }

        BoxBoundsHandle handle = partHandles[handleIndex];
        Transform partTransform = box.transform;

        using (new Handles.DrawingScope(
                   Handles.color,
                   Matrix4x4.TRS(
                       partTransform.position,
                       partTransform.rotation,
                       partTransform.lossyScale)))
        {
            handle.center = box.center;
            handle.size = box.size;

            EditorGUI.BeginChangeCheck();
            handle.DrawHandle();

            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            Undo.RecordObject(box, "Resize Room Volume Part");
            box.center = handle.center;
            box.size = handle.size;
            EditorUtility.SetDirty(box);
        }
    }

    // One row per part with a padlock. A part that was placed by hand, where
    // no probe would get it right, is otherwise wiped by the next press of
    // the fit button.
    private void DrawPartLocks()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Parts", EditorStyles.miniBoldLabel);

        Collider[] parts =
            Room.GetComponentsInChildren<Collider>(includeInactive: true);

        if (parts.Length == 0)
        {
            return;
        }

        for (int i = 0; i < parts.Length; i++)
        {
            Collider part = parts[i];

            if (part == null)
            {
                continue;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                bool locked = Room.IsPartLocked(part);
                bool nowLocked = GUILayout.Toggle(
                    locked,
                    locked ? "Locked" : "Fits",
                    EditorStyles.miniButton,
                    GUILayout.Width(60f)
                );

                if (nowLocked != locked)
                {
                    RoomVolumeSetupUtility.SetPartLocked(Room, part, nowLocked);
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(
                        part.gameObject, typeof(GameObject), true);
                }

                if (GUILayout.Button("Select", GUILayout.Width(55f)))
                {
                    Selection.activeGameObject = part.gameObject;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
            }
        }
    }

    private void DrawShape()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.PropertyField(volumeColliders, true);

        if (volumeColliders.arraySize == 0)
        {
            EditorGUILayout.LabelField(
                " ",
                "Empty means every collider under this object is used.",
                EditorStyles.miniLabel
            );
        }
    }
}
