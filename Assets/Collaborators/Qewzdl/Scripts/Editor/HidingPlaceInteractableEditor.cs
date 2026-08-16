using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HidingPlaceInteractable))]
public sealed class HidingPlaceInteractableEditor : Editor
{
    private static readonly Color InteractionColor = new(1f, 0.8f, 0.1f);
    private static readonly Color HidingColor = new(0.2f, 0.9f, 0.25f);
    private static readonly Color CameraColor = new(1f, 0.25f, 0.85f);
    private static readonly Color ExitColor = new(0.1f, 0.9f, 1f);
    private static readonly Color FallbackColor = new(0.1f, 0.55f, 1f);

    private const string DefaultDataDirectory =
        "Assets/Collaborators/Qewzdl/Configs/Hiding";

    private SerializedProperty data;
    private SerializedProperty interactionAnchor;
    private SerializedProperty hidingPoint;
    private SerializedProperty cameraAnchor;
    private SerializedProperty exitPoint;
    private SerializedProperty fallbackExitPoints;

    private Transform activeAnchor;
    private string activeAnchorLabel;
    private Color activeAnchorColor;

    private HidingPlaceInteractable HidingPlace =>
        (HidingPlaceInteractable)target;

    private void OnEnable()
    {
        data = serializedObject.FindProperty("data");
        interactionAnchor = serializedObject.FindProperty(
            HidingPlaceSetupUtility.InteractionAnchorProperty
        );
        hidingPoint = serializedObject.FindProperty(
            HidingPlaceSetupUtility.HidingPointProperty
        );
        cameraAnchor = serializedObject.FindProperty(
            HidingPlaceSetupUtility.CameraAnchorProperty
        );
        exitPoint = serializedObject.FindProperty(
            HidingPlaceSetupUtility.ExitPointProperty
        );
        fallbackExitPoints = serializedObject.FindProperty(
            HidingPlaceSetupUtility.FallbackExitPointsProperty
        );
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawIntroduction();
        DrawDataField();
        DrawSetupStatus();
        DrawSetupActions();
        DrawAnchorSection();
        DrawRuntimeState();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawIntroduction()
    {
        EditorGUILayout.HelpBox(
            "Anchors are child Transforms of the hiding place. " +
            "Create the missing anchors, then use Edit in Scene to move " +
            "them with the colored handles. Exit Point and fallback exits " +
            "mark the floor below the player; runtime accounts for the " +
            "player collider height.",
            MessageType.Info
        );
    }

    private void DrawDataField()
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField(
            "Behavior Settings",
            EditorStyles.boldLabel
        );

        HidingPlaceData currentData =
            data.objectReferenceValue as HidingPlaceData;
        HidingPlaceData nextData = EditorGUILayout.ObjectField(
            new GUIContent(
                "Hiding Place Data",
                "Transition timing, camera, noise, pose and enemy rules."
            ),
            currentData,
            typeof(HidingPlaceData),
            allowSceneObjects: false
        ) as HidingPlaceData;

        if (nextData != currentData)
        {
            data.objectReferenceValue = nextData;
        }

        using (new EditorGUI.DisabledScope(nextData != null))
        {
            if (GUILayout.Button("Create and Assign Hiding Place Data"))
            {
                CreateAndAssignData();
            }
        }
    }

    private void DrawSetupStatus()
    {
        serializedObject.ApplyModifiedProperties();
        IReadOnlyList<string> problems =
            HidingPlaceSetupUtility.GetSetupProblems(HidingPlace);
        serializedObject.Update();

        EditorGUILayout.Space(6f);
        if (problems.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "The hiding place is ready to use.",
                MessageType.Info
            );
            return;
        }

        string message = "Setup issues:";
        for (int i = 0; i < problems.Count; i++)
        {
            message += $"\n• {problems[i]}";
        }

        EditorGUILayout.HelpBox(message, MessageType.Warning);
    }

    private void DrawSetupActions()
    {
        EditorGUILayout.Space(4f);

        if (GUILayout.Button(
                "Create Missing Anchors and Components",
                GUILayout.Height(30f)))
        {
            ApplyAndRunSetup(repositionExistingAnchors: false);
        }

        if (GUILayout.Button("Auto-Position All Anchors"))
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Auto-Position Hiding Place Anchors",
                "The current position and rotation of every anchor will " +
                "be replaced with values calculated from the hiding " +
                "place Collider.",
                "Auto-Position",
                "Cancel"
            );

            if (confirmed)
            {
                ApplyAndRunSetup(repositionExistingAnchors: true);
            }
        }
    }

    private void DrawAnchorSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField(
            "Hiding Place Anchors",
            EditorStyles.boldLabel
        );

        DrawAnchorRow(
            interactionAnchor,
            "Interaction Anchor",
            "The server measures interaction distance from this point. " +
            "Usually this is the hiding place root.",
            InteractionColor
        );
        DrawAnchorRow(
            hidingPoint,
            "Hiding Point",
            "The player's feet/root position inside the cabinet or bed.",
            HidingColor
        );
        DrawAnchorRow(
            cameraAnchor,
            "Camera Anchor",
            "The eye position. The Transform's blue axis must face the " +
            "initial viewing direction inside the hiding place.",
            CameraColor
        );
        DrawAnchorRow(
            exitPoint,
            "Exit Point",
            "The primary free position where the server places the player.",
            ExitColor
        );

        DrawFallbackExits();
        DrawActiveAnchorTools();
    }

    private void DrawAnchorRow(
        SerializedProperty property,
        string label,
        string description,
        Color color
    )
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        DrawColorHeader(label, color);
        EditorGUILayout.LabelField(
            description,
            EditorStyles.wordWrappedMiniLabel
        );

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(
            property,
            GUIContent.none
        );

        Transform anchor = property.objectReferenceValue as Transform;
        using (new EditorGUI.DisabledScope(anchor == null))
        {
            if (GUILayout.Button("Edit in Scene", GUILayout.Width(105f)))
            {
                ActivateAnchor(anchor, label, color);
            }
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawFallbackExits()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        DrawColorHeader("Fallback Exit Points", FallbackColor);
        EditorGUILayout.LabelField(
            "Fallback exits. The server selects the first point that is " +
            "not blocked by a wall, door, item or another player.",
            EditorStyles.wordWrappedMiniLabel
        );

        for (int i = 0; i < fallbackExitPoints.arraySize; i++)
        {
            SerializedProperty element = fallbackExitPoints
                .GetArrayElementAtIndex(i);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(
                element,
                new GUIContent($"Exit {i + 1}")
            );

            Transform anchor = element.objectReferenceValue as Transform;
            using (new EditorGUI.DisabledScope(anchor == null))
            {
                if (GUILayout.Button("Edit", GUILayout.Width(50f)))
                {
                    ActivateAnchor(
                        anchor,
                        $"Fallback Exit {i + 1}",
                        FallbackColor
                    );
                }
            }

            if (GUILayout.Button("−", GUILayout.Width(24f)))
            {
                int previousSize = fallbackExitPoints.arraySize;
                fallbackExitPoints.DeleteArrayElementAtIndex(i);
                if (fallbackExitPoints.arraySize == previousSize)
                {
                    fallbackExitPoints.DeleteArrayElementAtIndex(i);
                }
                break;
            }

            EditorGUILayout.EndHorizontal();
        }

        if (GUILayout.Button("Add Fallback Exit"))
        {
            serializedObject.ApplyModifiedProperties();
            Transform anchor = HidingPlaceSetupUtility.AddFallbackExit(
                HidingPlace
            );
            serializedObject.Update();
            ActivateAnchor(
                anchor,
                "Fallback Exit",
                FallbackColor
            );
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawActiveAnchorTools()
    {
        if (activeAnchor == null)
        {
            return;
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(
            $"Editing: {activeAnchorLabel}",
            EditorStyles.boldLabel
        );

        if (activeAnchor == HidingPlace.transform)
        {
            EditorGUILayout.HelpBox(
                "The Interaction Anchor is the root Transform. Moving it " +
                "will move the entire hiding place. Create a separate " +
                "child Transform if you need a different interaction center.",
                MessageType.Info
            );
        }
        else
        {
            EditorGUILayout.LabelField(
                "Use the Scene View handles to move and rotate this anchor.",
                EditorStyles.wordWrappedMiniLabel
            );
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Focus in Scene View"))
        {
            FocusSceneView(activeAnchor);
        }

        if (activeAnchor == cameraAnchor.objectReferenceValue &&
            GUILayout.Button("Use Scene Camera Pose"))
        {
            AlignCameraAnchorToSceneView();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawRuntimeState()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.EnumPopup("State", HidingPlace.State);
            EditorGUILayout.Toggle("Occupied", HidingPlace.IsOccupied);
            EditorGUILayout.Toggle("Open", HidingPlace.IsOpen);
        }
    }

    private void CreateAndAssignData()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Create Hiding Place Settings",
            $"{HidingPlace.name} Data",
            "asset",
            "Choose where to save the HidingPlaceData asset.",
            DefaultDataDirectory
        );

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        HidingPlaceData asset = CreateInstance<HidingPlaceData>();
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();

        serializedObject.Update();
        data.objectReferenceValue = asset;
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(HidingPlace);
        PrefabUtility.RecordPrefabInstancePropertyModifications(HidingPlace);
        EditorGUIUtility.PingObject(asset);
    }

    private void ApplyAndRunSetup(bool repositionExistingAnchors)
    {
        serializedObject.ApplyModifiedProperties();
        HidingPlaceSetupUtility.EnsureCompleteSetup(
            HidingPlace,
            repositionExistingAnchors
        );
        serializedObject.Update();
        SceneView.RepaintAll();
    }

    private void ActivateAnchor(
        Transform anchor,
        string label,
        Color color
    )
    {
        activeAnchor = anchor;
        activeAnchorLabel = label;
        activeAnchorColor = color;
        FocusSceneView(anchor);
        Repaint();
        SceneView.RepaintAll();
    }

    private static void FocusSceneView(Transform anchor)
    {
        if (anchor == null || SceneView.lastActiveSceneView == null)
        {
            return;
        }

        SceneView.lastActiveSceneView.Frame(
            new Bounds(anchor.position, Vector3.one),
            instant: false
        );
        Tools.current = Tool.Move;
    }

    private void AlignCameraAnchorToSceneView()
    {
        Transform anchor = cameraAnchor.objectReferenceValue as Transform;
        Camera sceneCamera = SceneView.lastActiveSceneView?.camera;

        if (anchor == null || sceneCamera == null)
        {
            return;
        }

        Undo.RecordObject(anchor, "Align Hiding Camera Anchor");
        anchor.SetPositionAndRotation(
            sceneCamera.transform.position,
            sceneCamera.transform.rotation
        );
        EditorUtility.SetDirty(anchor);
        PrefabUtility.RecordPrefabInstancePropertyModifications(anchor);
        SceneView.RepaintAll();
    }

    private static void DrawColorHeader(string label, Color color)
    {
        Rect rect = EditorGUILayout.GetControlRect(false, 18f);
        Rect colorRect = new(rect.x, rect.y + 3f, 12f, 12f);
        EditorGUI.DrawRect(colorRect, color);
        EditorGUI.LabelField(
            new Rect(rect.x + 18f, rect.y, rect.width - 18f, rect.height),
            label,
            EditorStyles.boldLabel
        );
    }

    private void OnSceneGUI()
    {
        serializedObject.Update();

        DrawSceneAnchor(
            interactionAnchor.objectReferenceValue as Transform,
            "Interaction",
            InteractionColor
        );
        DrawSceneAnchor(
            hidingPoint.objectReferenceValue as Transform,
            "Hiding Point",
            HidingColor
        );
        DrawSceneAnchor(
            cameraAnchor.objectReferenceValue as Transform,
            "Camera Anchor",
            CameraColor
        );
        DrawSceneAnchor(
            exitPoint.objectReferenceValue as Transform,
            "Exit Point",
            ExitColor
        );

        for (int i = 0; i < fallbackExitPoints.arraySize; i++)
        {
            DrawSceneAnchor(
                fallbackExitPoints
                    .GetArrayElementAtIndex(i)
                    .objectReferenceValue as Transform,
                $"Fallback {i + 1}",
                FallbackColor
            );
        }

        DrawActiveAnchorHandle();
    }

    private void DrawSceneAnchor(
        Transform anchor,
        string label,
        Color color
    )
    {
        if (anchor == null)
        {
            return;
        }

        float size = HandleUtility.GetHandleSize(anchor.position);
        Handles.color = color;
        Handles.DrawDottedLine(
            HidingPlace.transform.position,
            anchor.position,
            4f
        );
        Handles.ArrowHandleCap(
            0,
            anchor.position,
            anchor.rotation,
            size * 0.45f,
            EventType.Repaint
        );
        Handles.Label(
            anchor.position + Vector3.up * size * 0.18f,
            label
        );

        if (Handles.Button(
                anchor.position,
                anchor.rotation,
                size * 0.09f,
                size * 0.12f,
                Handles.SphereHandleCap))
        {
            ActivateAnchor(anchor, label, color);
        }
    }

    private void DrawActiveAnchorHandle()
    {
        if (activeAnchor == null ||
            activeAnchor == HidingPlace.transform)
        {
            return;
        }

        Handles.color = activeAnchorColor;
        EditorGUI.BeginChangeCheck();
        Vector3 nextPosition = Handles.PositionHandle(
            activeAnchor.position,
            activeAnchor.rotation
        );
        Quaternion nextRotation = Handles.RotationHandle(
            activeAnchor.rotation,
            nextPosition
        );

        if (!EditorGUI.EndChangeCheck())
        {
            return;
        }

        Undo.RecordObject(activeAnchor, "Move Hiding Anchor");
        activeAnchor.SetPositionAndRotation(nextPosition, nextRotation);
        EditorUtility.SetDirty(activeAnchor);
        PrefabUtility.RecordPrefabInstancePropertyModifications(activeAnchor);
    }
}
