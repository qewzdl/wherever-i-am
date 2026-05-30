using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EscapePointAuthoring))]
public class EscapePointAuthoringEditor : Editor
{
    private SerializedProperty triggerColliderProperty;
    private SerializedProperty disableAfterVictoryProperty;
    private SerializedProperty runtimeTriggerProperty;

    private bool showGeneratedRuntime;

    private void OnEnable()
    {
        triggerColliderProperty = serializedObject.FindProperty("triggerCollider");
        disableAfterVictoryProperty = serializedObject.FindProperty("disableAfterVictory");
        runtimeTriggerProperty = serializedObject.FindProperty("runtimeTrigger");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Escape Point Setup", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(triggerColliderProperty);
        EditorGUILayout.PropertyField(disableAfterVictoryProperty);

        serializedObject.ApplyModifiedProperties();

        GUILayout.Space(10f);

        if (GUILayout.Button("Apply Escape Point Runtime Setup"))
        {
            VictoryAuthoringEditorUtility.ApplyEscapePointSetup((EscapePointAuthoring)target);
            serializedObject.Update();
        }

        GUILayout.Space(10f);

        showGeneratedRuntime = EditorGUILayout.Foldout(showGeneratedRuntime, "Generated Runtime", true);

        if (showGeneratedRuntime)
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.PropertyField(runtimeTriggerProperty);
            EditorGUI.EndDisabledGroup();
        }
    }
}