using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VictorySystemAuthoring))]
public class VictorySystemAuthoringEditor : Editor
{
    private SerializedProperty victoryModeProperty;
    private SerializedProperty escapePointProperty;
    private SerializedProperty objectivesProperty;
    private SerializedProperty runtimeOutcomeProperty;
    private SerializedProperty objectivesRootProperty;
    private SerializedProperty escapeRootProperty;

    private bool showGeneratedRuntime;

    private void OnEnable()
    {
        victoryModeProperty = serializedObject.FindProperty("victoryMode");
        escapePointProperty = serializedObject.FindProperty("escapePoint");
        objectivesProperty = serializedObject.FindProperty("objectives");
        runtimeOutcomeProperty = serializedObject.FindProperty("runtimeOutcome");
        objectivesRootProperty = serializedObject.FindProperty("objectivesRoot");
        escapeRootProperty = serializedObject.FindProperty("escapeRoot");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Victory Setup", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(victoryModeProperty);
        EditorGUILayout.PropertyField(escapePointProperty);
        EditorGUILayout.PropertyField(objectivesProperty, new GUIContent("Objectives"), true);

        serializedObject.ApplyModifiedProperties();

        GUILayout.Space(10f);

        DrawAuthoringActions();

        GUILayout.Space(10f);

        DrawGeneratedRuntime();
    }

    private void DrawAuthoringActions()
    {
        VictorySystemAuthoring authoring = (VictorySystemAuthoring)target;

        EditorGUILayout.LabelField("Authoring Actions", EditorStyles.boldLabel);

        if (GUILayout.Button("Create Missing Setup"))
        {
            VictoryAuthoringEditorUtility.CreateMissingSetup(authoring);
            serializedObject.Update();
        }

        if (GUILayout.Button("Create New Objective"))
        {
            VictoryAuthoringEditorUtility.CreateNewObjective(authoring);
            serializedObject.Update();
        }

        if (GUILayout.Button("Add Selected Objects As Objectives"))
        {
            VictoryAuthoringEditorUtility.AddSelectedObjectsAsObjectives(authoring);
            serializedObject.Update();
        }

        if (GUILayout.Button("Collect Child Objectives"))
        {
            VictoryAuthoringEditorUtility.CollectChildObjectives(authoring);
            serializedObject.Update();
        }

        if (GUILayout.Button("Apply Setup"))
        {
            VictoryAuthoringEditorUtility.ApplySetup(authoring);
            serializedObject.Update();
        }
    }

    private void DrawGeneratedRuntime()
    {
        showGeneratedRuntime = EditorGUILayout.Foldout(showGeneratedRuntime, "Generated Runtime", true);

        if (!showGeneratedRuntime)
            return;

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.PropertyField(runtimeOutcomeProperty);
        EditorGUILayout.PropertyField(objectivesRootProperty);
        EditorGUILayout.PropertyField(escapeRootProperty);
        EditorGUI.EndDisabledGroup();
    }
}