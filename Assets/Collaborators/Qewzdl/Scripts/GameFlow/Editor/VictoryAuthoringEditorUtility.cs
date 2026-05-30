using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class VictoryAuthoringEditorUtility
{
    private const string ObjectivesRootName = "Objectives";
    private const string EscapeRootName = "Escape";
    private const string EscapePointName = "EscapePoint";
    private const string ObjectiveNamePrefix = "Objective_";

    [MenuItem("Tools/Wherever I Am/Create Victory System")]
    public static void CreateVictorySystemFromMenu()
    {
        GameObject gameObject = new("VictorySystem");
        Undo.RegisterCreatedObjectUndo(gameObject, "Create Victory System");

        VictorySystemAuthoring authoring = Undo.AddComponent<VictorySystemAuthoring>(gameObject);
        CreateMissingSetup(authoring);

        Selection.activeGameObject = gameObject;
        MarkSceneDirty(gameObject);
    }

    public static void CreateMissingSetup(VictorySystemAuthoring authoring)
    {
        if (authoring == null)
            return;

        NetworkGameOutcome outcome = EnsureVictoryRuntime(authoring);
        Transform objectivesRoot = EnsureObjectivesRoot(authoring);
        Transform escapeRoot = EnsureEscapeRoot(authoring);

        SetObjectReference(authoring, "runtimeOutcome", outcome);
        SetObjectReference(authoring, "objectivesRoot", objectivesRoot);
        SetObjectReference(authoring, "escapeRoot", escapeRoot);

        EscapePointAuthoring escapePoint = authoring.EscapePoint;

        if (escapePoint == null)
            escapePoint = CreateEscapePoint(authoring, escapeRoot);

        ConfigureEscapePoint(escapePoint, outcome);
        SetObjectReference(authoring, "escapePoint", escapePoint);

        ApplySetup(authoring);
        Selection.activeGameObject = authoring.gameObject;
    }

    public static VictoryObjectiveAuthoring CreateNewObjective(VictorySystemAuthoring authoring)
    {
        if (authoring == null)
            return null;

        EnsureVictoryRuntime(authoring);
        Transform objectivesRoot = EnsureObjectivesRoot(authoring);

        string objectName = GetUniqueChildName(objectivesRoot, ObjectiveNamePrefix + "New");
        GameObject objectiveObject = new(objectName);

        Undo.RegisterCreatedObjectUndo(objectiveObject, "Create Victory Objective");
        Undo.SetTransformParent(objectiveObject.transform, objectivesRoot, "Parent Victory Objective");

        VictoryObjectiveAuthoring objective = EnsureComponent<VictoryObjectiveAuthoring>(objectiveObject);
        EnsureObjectiveRuntime(objective);
        AddObjectiveToSystem(authoring, objective);
        ApplySetup(authoring);

        Selection.activeGameObject = objectiveObject;
        MarkSceneDirty(authoring.gameObject);

        return objective;
    }

    public static int AddSelectedObjectsAsObjectives(VictorySystemAuthoring authoring)
    {
        if (authoring == null)
            return 0;

        GameObject[] selectedObjects = Selection.gameObjects;
        int addedCount = 0;

        for (int i = 0; i < selectedObjects.Length; i++)
        {
            GameObject selectedObject = selectedObjects[i];

            if (selectedObject == null)
                continue;

            if (selectedObject == authoring.gameObject)
                continue;

            VictoryObjectiveAuthoring objective = EnsureComponent<VictoryObjectiveAuthoring>(selectedObject);
            EnsureObjectiveRuntime(objective);

            if (AddObjectiveToSystem(authoring, objective))
                addedCount++;
        }

        ApplySetup(authoring);
        MarkSceneDirty(authoring.gameObject);

        return addedCount;
    }

    public static int CollectChildObjectives(VictorySystemAuthoring authoring)
    {
        if (authoring == null)
            return 0;

        VictoryObjectiveAuthoring[] childObjectives = authoring.GetComponentsInChildren<VictoryObjectiveAuthoring>(true);

        SerializedObject serializedAuthoring = new(authoring);
        SerializedProperty objectivesProperty = serializedAuthoring.FindProperty("objectives");

        objectivesProperty.ClearArray();

        int addedCount = 0;

        for (int i = 0; i < childObjectives.Length; i++)
        {
            VictoryObjectiveAuthoring objective = childObjectives[i];

            if (objective == null)
                continue;

            EnsureObjectiveRuntime(objective);

            objectivesProperty.InsertArrayElementAtIndex(addedCount);
            objectivesProperty.GetArrayElementAtIndex(addedCount).objectReferenceValue = objective;
            addedCount++;
        }

        serializedAuthoring.ApplyModifiedProperties();

        ApplySetup(authoring);
        MarkSceneDirty(authoring.gameObject);

        return addedCount;
    }

    public static void ApplySetup(VictorySystemAuthoring authoring)
    {
        if (authoring == null)
            return;

        NetworkGameOutcome outcome = EnsureVictoryRuntime(authoring);
        List<NetworkVictoryObjective> requiredRuntimeObjectives = new();

        IReadOnlyList<VictoryObjectiveAuthoring> objectives = authoring.Objectives;

        for (int i = 0; i < objectives.Count; i++)
        {
            VictoryObjectiveAuthoring objective = objectives[i];

            if (objective == null)
                continue;

            NetworkVictoryObjective runtimeObjective = EnsureObjectiveRuntime(objective);

            if (objective.IsRequired && runtimeObjective != null)
                requiredRuntimeObjectives.Add(runtimeObjective);
        }

        ConfigureOutcome(outcome, authoring.VictoryMode, requiredRuntimeObjectives.ToArray());

        if (authoring.EscapePoint != null)
            ConfigureEscapePoint(authoring.EscapePoint, outcome);

        SetObjectReference(authoring, "runtimeOutcome", outcome);
        MarkSceneDirty(authoring.gameObject);
    }

    public static void ApplyObjectiveSetup(VictoryObjectiveAuthoring objective)
    {
        if (objective == null)
            return;

        EnsureObjectiveRuntime(objective);
        MarkSceneDirty(objective.gameObject);
    }

    public static void ApplyEscapePointSetup(EscapePointAuthoring escapePoint)
    {
        if (escapePoint == null)
            return;

        VictorySystemAuthoring system = FindSingleVictorySystem();

        if (system == null)
        {
            Debug.LogError($"{nameof(EscapePointAuthoring)} requires a {nameof(VictorySystemAuthoring)} in the scene to apply setup.", escapePoint);
            return;
        }

        NetworkGameOutcome outcome = EnsureVictoryRuntime(system);
        ConfigureEscapePoint(escapePoint, outcome);
        SetObjectReference(system, "escapePoint", escapePoint);
        ApplySetup(system);
        MarkSceneDirty(escapePoint.gameObject);
    }

    private static NetworkGameOutcome EnsureVictoryRuntime(VictorySystemAuthoring authoring)
    {
        EnsureComponent<NetworkObject>(authoring.gameObject);

        NetworkGameOutcome outcome = authoring.GetComponent<NetworkGameOutcome>();

        if (outcome == null)
            outcome = Undo.AddComponent<NetworkGameOutcome>(authoring.gameObject);

        SetObjectReference(authoring, "runtimeOutcome", outcome);

        return outcome;
    }

    private static NetworkVictoryObjective EnsureObjectiveRuntime(VictoryObjectiveAuthoring objective)
    {
        EnsureComponent<NetworkObject>(objective.gameObject);

        NetworkVictoryObjective runtimeObjective = objective.GetComponent<NetworkVictoryObjective>();

        if (runtimeObjective == null)
            runtimeObjective = Undo.AddComponent<NetworkVictoryObjective>(objective.gameObject);

        EnsureObjectiveAuthoringDefaults(objective);
        ConfigureRuntimeObjective(objective, runtimeObjective);
        SetObjectReference(objective, "runtimeObjective", runtimeObjective);

        return runtimeObjective;
    }

    private static EscapePointAuthoring CreateEscapePoint(VictorySystemAuthoring authoring, Transform escapeRoot)
    {
        GameObject escapeObject = new(GetUniqueChildName(escapeRoot, EscapePointName));

        Undo.RegisterCreatedObjectUndo(escapeObject, "Create Escape Point");
        Undo.SetTransformParent(escapeObject.transform, escapeRoot, "Parent Escape Point");

        BoxCollider boxCollider = Undo.AddComponent<BoxCollider>(escapeObject);
        boxCollider.isTrigger = true;
        boxCollider.size = new Vector3(2f, 2f, 1f);

        EscapePointAuthoring escapePoint = Undo.AddComponent<EscapePointAuthoring>(escapeObject);
        SetObjectReference(escapePoint, "triggerCollider", boxCollider);

        MarkSceneDirty(authoring.gameObject);

        return escapePoint;
    }

    private static void ConfigureEscapePoint(EscapePointAuthoring escapePoint, NetworkGameOutcome outcome)
    {
        if (escapePoint == null)
            return;

        EnsureComponent<NetworkObject>(escapePoint.gameObject);

        Collider triggerCollider = escapePoint.TriggerCollider;

        if (triggerCollider == null)
            triggerCollider = escapePoint.GetComponent<Collider>();

        if (triggerCollider == null)
        {
            BoxCollider boxCollider = Undo.AddComponent<BoxCollider>(escapePoint.gameObject);
            boxCollider.size = new Vector3(2f, 2f, 1f);
            triggerCollider = boxCollider;
        }

        triggerCollider.isTrigger = true;

        EscapeVictoryTrigger runtimeTrigger = escapePoint.GetComponent<EscapeVictoryTrigger>();

        if (runtimeTrigger == null)
            runtimeTrigger = Undo.AddComponent<EscapeVictoryTrigger>(escapePoint.gameObject);

        SetObjectReference(escapePoint, "triggerCollider", triggerCollider);
        SetObjectReference(escapePoint, "runtimeTrigger", runtimeTrigger);

        SerializedObject serializedTrigger = new(runtimeTrigger);
        SetSerializedObjectReference(serializedTrigger, "gameOutcome", outcome);
        SetSerializedBool(serializedTrigger, "disableAfterVictory", escapePoint.DisableAfterVictory);
        serializedTrigger.ApplyModifiedProperties();

        EditorUtility.SetDirty(triggerCollider);
        EditorUtility.SetDirty(runtimeTrigger);
        EditorUtility.SetDirty(escapePoint);
    }

    private static void ConfigureOutcome(NetworkGameOutcome outcome, EscapeVictoryMode victoryMode, NetworkVictoryObjective[] requiredObjectives)
    {
        SerializedObject serializedOutcome = new(outcome);

        SetSerializedEnum(serializedOutcome, "victoryMode", (int)victoryMode);

        SerializedProperty objectivesProperty = serializedOutcome.FindProperty("requiredObjectives");
        objectivesProperty.arraySize = requiredObjectives.Length;

        for (int i = 0; i < requiredObjectives.Length; i++)
            objectivesProperty.GetArrayElementAtIndex(i).objectReferenceValue = requiredObjectives[i];

        serializedOutcome.ApplyModifiedProperties();
        EditorUtility.SetDirty(outcome);
    }

    private static void ConfigureRuntimeObjective(VictoryObjectiveAuthoring objective, NetworkVictoryObjective runtimeObjective)
    {
        SerializedObject serializedObjective = new(runtimeObjective);

        SetSerializedString(serializedObjective, "objectiveId", objective.ObjectiveId);
        SetSerializedBool(serializedObjective, "completedOnServerSpawn", objective.StartsCompleted);

        serializedObjective.ApplyModifiedProperties();
        EditorUtility.SetDirty(runtimeObjective);
    }

    private static void EnsureObjectiveAuthoringDefaults(VictoryObjectiveAuthoring objective)
    {
        SerializedObject serializedObjective = new(objective);

        SerializedProperty objectiveIdProperty = serializedObjective.FindProperty("objectiveId");
        SerializedProperty displayNameProperty = serializedObjective.FindProperty("displayName");

        if (string.IsNullOrWhiteSpace(objectiveIdProperty.stringValue))
            objectiveIdProperty.stringValue = VictoryObjectiveAuthoring.CreateStableId(objective.gameObject.name);

        if (string.IsNullOrWhiteSpace(displayNameProperty.stringValue))
            displayNameProperty.stringValue = ObjectNames.NicifyVariableName(objective.gameObject.name);

        serializedObjective.ApplyModifiedProperties();
        EditorUtility.SetDirty(objective);
    }

    private static Transform EnsureObjectivesRoot(VictorySystemAuthoring authoring)
    {
        Transform root = authoring.ObjectivesRoot;

        if (root == null)
            root = EnsureChild(authoring.transform, ObjectivesRootName);

        SetObjectReference(authoring, "objectivesRoot", root);
        return root;
    }

    private static Transform EnsureEscapeRoot(VictorySystemAuthoring authoring)
    {
        Transform root = authoring.EscapeRoot;

        if (root == null)
            root = EnsureChild(authoring.transform, EscapeRootName);

        SetObjectReference(authoring, "escapeRoot", root);
        return root;
    }

    private static Transform EnsureChild(Transform parent, string childName)
    {
        Transform existingChild = parent.Find(childName);

        if (existingChild != null)
            return existingChild;

        GameObject childObject = new(childName);

        Undo.RegisterCreatedObjectUndo(childObject, $"Create {childName}");
        Undo.SetTransformParent(childObject.transform, parent, $"Parent {childName}");
        childObject.transform.localPosition = Vector3.zero;
        childObject.transform.localRotation = Quaternion.identity;
        childObject.transform.localScale = Vector3.one;

        return childObject.transform;
    }

    private static bool AddObjectiveToSystem(VictorySystemAuthoring authoring, VictoryObjectiveAuthoring objective)
    {
        SerializedObject serializedAuthoring = new(authoring);
        SerializedProperty objectivesProperty = serializedAuthoring.FindProperty("objectives");

        for (int i = 0; i < objectivesProperty.arraySize; i++)
        {
            if (objectivesProperty.GetArrayElementAtIndex(i).objectReferenceValue == objective)
                return false;
        }

        int index = objectivesProperty.arraySize;
        objectivesProperty.InsertArrayElementAtIndex(index);
        objectivesProperty.GetArrayElementAtIndex(index).objectReferenceValue = objective;

        serializedAuthoring.ApplyModifiedProperties();
        EditorUtility.SetDirty(authoring);

        return true;
    }

    private static T EnsureComponent<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();

        if (component != null)
            return component;

        return Undo.AddComponent<T>(gameObject);
    }

    private static VictorySystemAuthoring FindSingleVictorySystem()
    {
        VictorySystemAuthoring[] systems = Object.FindObjectsByType<VictorySystemAuthoring>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        if (systems.Length == 1)
            return systems[0];

        return null;
    }

    private static string GetUniqueChildName(Transform parent, string baseName)
    {
        if (parent.Find(baseName) == null)
            return baseName;

        int index = 1;

        while (parent.Find(baseName + "_" + index) != null)
            index++;

        return baseName + "_" + index;
    }

    private static void SetObjectReference(Object target, string propertyName, Object value)
    {
        SerializedObject serializedObject = new(target);
        SetSerializedObjectReference(serializedObject, propertyName, value);
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
    }

    private static void SetSerializedObjectReference(SerializedObject serializedObject, string propertyName, Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);

        if (property == null)
        {
            Debug.LogError($"Serialized property '{propertyName}' was not found on {serializedObject.targetObject.name}.", serializedObject.targetObject);
            return;
        }

        property.objectReferenceValue = value;
    }

    private static void SetSerializedString(SerializedObject serializedObject, string propertyName, string value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);

        if (property == null)
        {
            Debug.LogError($"Serialized property '{propertyName}' was not found on {serializedObject.targetObject.name}.", serializedObject.targetObject);
            return;
        }

        property.stringValue = value;
    }

    private static void SetSerializedBool(SerializedObject serializedObject, string propertyName, bool value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);

        if (property == null)
        {
            Debug.LogError($"Serialized property '{propertyName}' was not found on {serializedObject.targetObject.name}.", serializedObject.targetObject);
            return;
        }

        property.boolValue = value;
    }

    private static void SetSerializedEnum(SerializedObject serializedObject, string propertyName, int value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);

        if (property == null)
        {
            Debug.LogError($"Serialized property '{propertyName}' was not found on {serializedObject.targetObject.name}.", serializedObject.targetObject);
            return;
        }

        property.enumValueIndex = value;
    }

    private static void MarkSceneDirty(GameObject gameObject)
    {
        if (gameObject == null)
            return;

        Scene scene = gameObject.scene;

        if (scene.IsValid())
            EditorSceneManager.MarkSceneDirty(scene);
    }
}