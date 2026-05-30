using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VictoryObjectiveAuthoring))]
public class VictoryObjectiveAuthoringEditor : Editor
{
    private SerializedProperty objectiveIdProperty;
    private SerializedProperty displayNameProperty;
    private SerializedProperty isRequiredProperty;
    private SerializedProperty startsCompletedProperty;
    private SerializedProperty runtimeObjectiveProperty;

    private bool showGeneratedRuntime;

    private void OnEnable()
    {
        objectiveIdProperty = serializedObject.FindProperty("objectiveId");
        displayNameProperty = serializedObject.FindProperty("displayName");
        isRequiredProperty = serializedObject.FindProperty("isRequired");
        startsCompletedProperty = serializedObject.FindProperty("startsCompleted");
        runtimeObjectiveProperty = serializedObject.FindProperty("runtimeObjective");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Objective Setup", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(objectiveIdProperty);
        EditorGUILayout.PropertyField(displayNameProperty);
        EditorGUILayout.PropertyField(isRequiredProperty);
        EditorGUILayout.PropertyField(startsCompletedProperty);

        serializedObject.ApplyModifiedProperties();

        GUILayout.Space(10f);

        if (GUILayout.Button("Apply Objective Runtime Setup"))
        {
            VictoryAuthoringEditorUtility.ApplyObjectiveSetup((VictoryObjectiveAuthoring)target);
            serializedObject.Update();
        }

        GUILayout.Space(10f);

        showGeneratedRuntime = EditorGUILayout.Foldout(showGeneratedRuntime, "Generated Runtime", true);

        if (showGeneratedRuntime)
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.PropertyField(runtimeObjectiveProperty);
            EditorGUI.EndDisabledGroup();
        }
    }
}