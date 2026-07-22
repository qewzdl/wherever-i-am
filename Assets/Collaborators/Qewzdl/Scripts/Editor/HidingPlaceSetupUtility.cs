using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

public static class HidingPlaceSetupUtility
{
    public const string InteractionAnchorProperty = "interactionAnchor";
    public const string HidingPointProperty = "hidingPoint";
    public const string CameraAnchorProperty = "cameraAnchor";
    public const string ExitPointProperty = "exitPoint";
    public const string FallbackExitPointsProperty = "fallbackExitPoints";

    public const string HidingPointName = "Hiding Point";
    public const string CameraAnchorName = "Camera Anchor";
    public const string ExitPointName = "Exit Point";
    public const string FallbackExitLeftName = "Fallback Exit Left";
    public const string FallbackExitRightName = "Fallback Exit Right";

    private const string SetupUndoName = "Setup Hiding Place";
    private const float MinimumExitClearance = 0.75f;

    [MenuItem(
        "GameObject/Wherever I Am/Hiding Place",
        false,
        10
    )]
    private static void CreateFromGameObjectMenu(MenuCommand command)
    {
        GameObject parent = command.context as GameObject;
        HidingPlaceInteractable hidingPlace = CreateInScene(
            parent != null ? parent.transform : null
        );

        Selection.activeGameObject = hidingPlace.gameObject;
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    [MenuItem(
        "CONTEXT/HidingPlaceInteractable/Create Missing Anchors",
        false,
        100
    )]
    private static void SetupFromContextMenu(MenuCommand command)
    {
        if (command.context is not HidingPlaceInteractable hidingPlace)
        {
            return;
        }

        EnsureCompleteSetup(
            hidingPlace,
            repositionExistingAnchors: false
        );
        Selection.activeGameObject = hidingPlace.gameObject;
    }

    public static HidingPlaceInteractable CreateInScene(
        Transform parent = null
    )
    {
        GameObject root = new("Hiding Place");
        Undo.RegisterCreatedObjectUndo(root, "Create Hiding Place");

        if (parent != null)
        {
            Undo.SetTransformParent(
                root.transform,
                parent,
                "Parent Hiding Place"
            );
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
        }

        int interactableLayer = LayerMask.NameToLayer("Interactable");
        if (interactableLayer >= 0)
        {
            root.layer = interactableLayer;
        }

        BoxCollider collider = Undo.AddComponent<BoxCollider>(root);
        collider.center = Vector3.up;
        collider.size = new Vector3(1f, 2f, 1f);

        HidingPlaceInteractable hidingPlace =
            Undo.AddComponent<HidingPlaceInteractable>(root);

        EnsureCompleteSetup(
            hidingPlace,
            repositionExistingAnchors: true
        );
        return hidingPlace;
    }

    public static void EnsureCompleteSetup(
        HidingPlaceInteractable hidingPlace,
        bool repositionExistingAnchors
    )
    {
        if (hidingPlace == null)
        {
            return;
        }

        GameObject root = hidingPlace.gameObject;
        EnsureRootComponent<NetworkObject>(root);
        EnsureRootCollider(root);
        EnsureRootComponent<HidingPlaceNavigationObstacle>(root);
        EnsureRootComponent<HidingPlacePresentation>(root);
        EnsureRootComponent<NetworkHidingGameplayNoiseEmitter>(root);

        Undo.RecordObject(hidingPlace, SetupUndoName);

        SerializedObject serialized = new(hidingPlace);
        SerializedProperty interaction = serialized.FindProperty(
            InteractionAnchorProperty
        );
        SerializedProperty hiding = serialized.FindProperty(
            HidingPointProperty
        );
        SerializedProperty camera = serialized.FindProperty(
            CameraAnchorProperty
        );
        SerializedProperty exit = serialized.FindProperty(
            ExitPointProperty
        );
        SerializedProperty fallback = serialized.FindProperty(
            FallbackExitPointsProperty
        );

        if (interaction.objectReferenceValue == null)
        {
            interaction.objectReferenceValue = root.transform;
        }

        HidingPlaceAnchorLayout layout = CalculateLayout(root.transform);

        Transform hidingPoint = GetOrCreateRequiredAnchor(
            root.transform,
            hiding,
            HidingPointName,
            out bool createdHidingPoint
        );
        Transform cameraAnchor = GetOrCreateRequiredAnchor(
            root.transform,
            camera,
            CameraAnchorName,
            out bool createdCameraAnchor
        );
        Transform exitPoint = GetOrCreateRequiredAnchor(
            root.transform,
            exit,
            ExitPointName,
            out bool createdExitPoint
        );

        ApplyAnchorPoseIfRequired(
            hidingPoint,
            layout.HidingPoint,
            Quaternion.identity,
            createdHidingPoint || repositionExistingAnchors
        );
        ApplyAnchorPoseIfRequired(
            cameraAnchor,
            layout.CameraAnchor,
            Quaternion.identity,
            createdCameraAnchor || repositionExistingAnchors
        );
        ApplyAnchorPoseIfRequired(
            exitPoint,
            layout.ExitPoint,
            Quaternion.identity,
            createdExitPoint || repositionExistingAnchors
        );

        EnsureFallbackExits(
            root.transform,
            fallback,
            layout,
            repositionExistingAnchors
        );

        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(hidingPlace);
        PrefabUtility.RecordPrefabInstancePropertyModifications(hidingPlace);
    }

    public static Transform AddFallbackExit(
        HidingPlaceInteractable hidingPlace
    )
    {
        if (hidingPlace == null)
        {
            return null;
        }

        SerializedObject serialized = new(hidingPlace);
        SerializedProperty fallback = serialized.FindProperty(
            FallbackExitPointsProperty
        );
        int index = fallback.arraySize;
        string anchorName = $"Fallback Exit {index + 1}";
        Transform anchor = CreateAnchor(
            hidingPlace.transform,
            anchorName
        );

        HidingPlaceAnchorLayout layout = CalculateLayout(
            hidingPlace.transform
        );
        bool placeOnLeft = index % 2 == 0;
        Vector3 position = placeOnLeft
            ? layout.FallbackExitLeft
            : layout.FallbackExitRight;
        float extraOffset = Mathf.Floor(index / 2f) * 0.75f;
        position.z += extraOffset;

        ApplyAnchorPoseIfRequired(
            anchor,
            position,
            Quaternion.Euler(0f, placeOnLeft ? -90f : 90f, 0f),
            shouldApply: true
        );

        Undo.RecordObject(hidingPlace, "Add Hiding Fallback Exit");
        fallback.arraySize++;
        fallback
            .GetArrayElementAtIndex(fallback.arraySize - 1)
            .objectReferenceValue = anchor;
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(hidingPlace);
        PrefabUtility.RecordPrefabInstancePropertyModifications(hidingPlace);
        return anchor;
    }

    public static IReadOnlyList<string> GetSetupProblems(
        HidingPlaceInteractable hidingPlace
    )
    {
        List<string> problems = new();

        if (hidingPlace == null)
        {
            problems.Add("The hiding place component is missing.");
            return problems;
        }

        GameObject root = hidingPlace.gameObject;
        SerializedObject serialized = new(hidingPlace);

        if (hidingPlace.Configuration == null)
        {
            problems.Add("Hiding Place Data is not assigned.");
        }

        AddMissingReferenceProblem(
            problems,
            serialized,
            HidingPointProperty,
            "Hiding Point is not assigned."
        );
        AddMissingReferenceProblem(
            problems,
            serialized,
            CameraAnchorProperty,
            "Camera Anchor is not assigned."
        );
        AddMissingReferenceProblem(
            problems,
            serialized,
            ExitPointProperty,
            "Exit Point is not assigned."
        );

        SerializedProperty fallback = serialized.FindProperty(
            FallbackExitPointsProperty
        );
        int validFallbackCount = 0;
        for (int i = 0; i < fallback.arraySize; i++)
        {
            if (fallback
                    .GetArrayElementAtIndex(i)
                    .objectReferenceValue != null)
            {
                validFallbackCount++;
            }
        }

        if (validFallbackCount < 2)
        {
            problems.Add(
                "Add at least two Fallback Exits for safe placement."
            );
        }

        if (root.GetComponent<Collider>() == null)
        {
            problems.Add("The root object has no Collider.");
        }

        HidingPlaceNavigationObstacle navigationObstacle =
            root.GetComponent<HidingPlaceNavigationObstacle>();
        if (navigationObstacle == null ||
            root.GetComponent<UnityEngine.AI.NavMeshObstacle>() == null)
        {
            problems.Add("Enemy navigation obstacle is missing.");
        }

        if (root.GetComponent<HidingPlacePresentation>() == null)
        {
            problems.Add("Hiding Place Presentation is missing.");
        }

        if (root.GetComponent<NetworkHidingGameplayNoiseEmitter>() == null)
        {
            problems.Add("Network Hiding Gameplay Noise Emitter is missing.");
        }

        int interactableLayer = LayerMask.NameToLayer("Interactable");
        if (interactableLayer >= 0 && root.layer != interactableLayer)
        {
            problems.Add("The root object must use the Interactable layer.");
        }

        return problems;
    }

    public static bool HasCompleteSetup(
        HidingPlaceInteractable hidingPlace
    )
    {
        return GetSetupProblems(hidingPlace).Count == 0;
    }

    public static HidingPlaceAnchorLayout CalculateLayout(
        Transform root
    )
    {
        Bounds bounds = CalculateLocalBounds(root);
        float floorY = bounds.min.y;
        float centerX = bounds.center.x;
        float centerZ = bounds.center.z;
        float height = Mathf.Max(0.1f, bounds.size.y);
        float exitZ = bounds.max.z + MinimumExitClearance;
        float leftX = bounds.min.x - MinimumExitClearance;
        float rightX = bounds.max.x + MinimumExitClearance;

        return new HidingPlaceAnchorLayout(
            new Vector3(centerX, floorY, centerZ),
            new Vector3(
                centerX,
                floorY + height * 0.8f,
                centerZ
            ),
            new Vector3(centerX, floorY, exitZ),
            new Vector3(leftX, floorY, centerZ),
            new Vector3(rightX, floorY, centerZ)
        );
    }

    private static void EnsureFallbackExits(
        Transform root,
        SerializedProperty fallback,
        HidingPlaceAnchorLayout layout,
        bool repositionExistingAnchors
    )
    {
        List<Transform> exits = new();

        for (int i = 0; i < fallback.arraySize; i++)
        {
            Transform existing = fallback
                .GetArrayElementAtIndex(i)
                .objectReferenceValue as Transform;

            if (existing != null && !exits.Contains(existing))
            {
                exits.Add(existing);
            }
        }

        Transform leftExit = GetOrCreateFallback(
            root,
            exits,
            0,
            FallbackExitLeftName,
            out bool createdLeftExit
        );
        Transform rightExit = GetOrCreateFallback(
            root,
            exits,
            1,
            FallbackExitRightName,
            out bool createdRightExit
        );

        ApplyAnchorPoseIfRequired(
            leftExit,
            layout.FallbackExitLeft,
            Quaternion.Euler(0f, -90f, 0f),
            createdLeftExit || repositionExistingAnchors
        );
        ApplyAnchorPoseIfRequired(
            rightExit,
            layout.FallbackExitRight,
            Quaternion.Euler(0f, 90f, 0f),
            createdRightExit || repositionExistingAnchors
        );

        fallback.arraySize = exits.Count;
        for (int i = 0; i < exits.Count; i++)
        {
            fallback
                .GetArrayElementAtIndex(i)
                .objectReferenceValue = exits[i];
        }
    }

    private static Transform GetOrCreateFallback(
        Transform root,
        List<Transform> exits,
        int index,
        string name,
        out bool created
    )
    {
        created = false;

        if (index < exits.Count && exits[index] != null)
        {
            return exits[index];
        }

        Transform anchor = FindDirectChild(root, name);

        if (anchor == null)
        {
            anchor = CreateAnchor(root, name);
            created = true;
        }

        if (!exits.Contains(anchor))
        {
            exits.Add(anchor);
        }

        return anchor;
    }

    private static Transform GetOrCreateRequiredAnchor(
        Transform root,
        SerializedProperty property,
        string name,
        out bool created
    )
    {
        Transform anchor = property.objectReferenceValue as Transform;
        created = false;

        if (anchor == null)
        {
            anchor = FindDirectChild(root, name);
        }

        if (anchor == null)
        {
            anchor = CreateAnchor(root, name);
            created = true;
        }

        property.objectReferenceValue = anchor;
        return anchor;
    }

    private static Transform CreateAnchor(
        Transform root,
        string name
    )
    {
        GameObject anchorObject = new(name);
        anchorObject.layer = root.gameObject.layer;
        Undo.RegisterCreatedObjectUndo(
            anchorObject,
            "Create Hiding Anchor"
        );
        Undo.SetTransformParent(
            anchorObject.transform,
            root,
            "Parent Hiding Anchor"
        );
        return anchorObject.transform;
    }

    private static Transform FindDirectChild(
        Transform parent,
        string childName
    )
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == childName)
            {
                return child;
            }
        }

        return null;
    }

    private static void ApplyAnchorPoseIfRequired(
        Transform anchor,
        Vector3 localPosition,
        Quaternion localRotation,
        bool shouldApply
    )
    {
        if (anchor == null || !shouldApply)
        {
            return;
        }

        Undo.RecordObject(anchor, "Position Hiding Anchor");
        anchor.localPosition = localPosition;
        anchor.localRotation = localRotation;
        anchor.localScale = Vector3.one;
        EditorUtility.SetDirty(anchor);
        PrefabUtility.RecordPrefabInstancePropertyModifications(anchor);
    }

    private static Bounds CalculateLocalBounds(Transform root)
    {
        if (root == null)
        {
            return new Bounds(Vector3.up, new Vector3(1f, 2f, 1f));
        }

        Collider[] rootColliders = root.GetComponents<Collider>();
        if (TryCalculateLocalBounds(root, rootColliders, out Bounds bounds))
        {
            return bounds;
        }

        Collider[] childColliders = root.GetComponentsInChildren<Collider>(
            includeInactive: true
        );
        if (TryCalculateLocalBounds(root, childColliders, out bounds))
        {
            return bounds;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(
            includeInactive: true
        );
        if (TryCalculateLocalBounds(root, renderers, out bounds))
        {
            return bounds;
        }

        return new Bounds(Vector3.up, new Vector3(1f, 2f, 1f));
    }

    private static bool TryCalculateLocalBounds(
        Transform root,
        Collider[] colliders,
        out Bounds bounds
    )
    {
        bounds = default;
        bool hasBounds = false;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || collider.isTrigger)
            {
                continue;
            }

            EncapsulateWorldBounds(
                root,
                collider.bounds,
                ref bounds,
                ref hasBounds
            );
        }

        return hasBounds;
    }

    private static bool TryCalculateLocalBounds(
        Transform root,
        Renderer[] renderers,
        out Bounds bounds
    )
    {
        bounds = default;
        bool hasBounds = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            EncapsulateWorldBounds(
                root,
                renderer.bounds,
                ref bounds,
                ref hasBounds
            );
        }

        return hasBounds;
    }

    private static void EncapsulateWorldBounds(
        Transform root,
        Bounds worldBounds,
        ref Bounds localBounds,
        ref bool hasBounds
    )
    {
        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;

        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                for (int z = 0; z < 2; z++)
                {
                    Vector3 worldPoint = new(
                        x == 0 ? min.x : max.x,
                        y == 0 ? min.y : max.y,
                        z == 0 ? min.z : max.z
                    );
                    Vector3 localPoint = root.InverseTransformPoint(
                        worldPoint
                    );

                    if (!hasBounds)
                    {
                        localBounds = new Bounds(localPoint, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localPoint);
                    }
                }
            }
        }
    }

    private static void AddMissingReferenceProblem(
        List<string> problems,
        SerializedObject serialized,
        string propertyName,
        string problem
    )
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property.objectReferenceValue == null)
        {
            problems.Add(problem);
        }
    }

    private static void EnsureRootCollider(GameObject root)
    {
        if (root.GetComponent<Collider>() == null)
        {
            Undo.AddComponent<BoxCollider>(root);
        }
    }

    private static TComponent EnsureRootComponent<TComponent>(
        GameObject root
    )
        where TComponent : Component
    {
        TComponent component = root.GetComponent<TComponent>();
        return component != null
            ? component
            : Undo.AddComponent<TComponent>(root);
    }
}

public readonly struct HidingPlaceAnchorLayout
{
    public Vector3 HidingPoint { get; }
    public Vector3 CameraAnchor { get; }
    public Vector3 ExitPoint { get; }
    public Vector3 FallbackExitLeft { get; }
    public Vector3 FallbackExitRight { get; }

    public HidingPlaceAnchorLayout(
        Vector3 hidingPoint,
        Vector3 cameraAnchor,
        Vector3 exitPoint,
        Vector3 fallbackExitLeft,
        Vector3 fallbackExitRight
    )
    {
        HidingPoint = hidingPoint;
        CameraAnchor = cameraAnchor;
        ExitPoint = exitPoint;
        FallbackExitLeft = fallbackExitLeft;
        FallbackExitRight = fallbackExitRight;
    }
}
