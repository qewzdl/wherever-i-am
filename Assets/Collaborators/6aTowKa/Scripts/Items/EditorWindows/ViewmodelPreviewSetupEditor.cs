using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ViewmodelPreviewSetup))]
public class ViewmodelPreviewSetupEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space(6);

        var setup = (ViewmodelPreviewSetup)target;

        if (GUILayout.Button("Refresh (read item)", GUILayout.Height(26)))
            setup.Refresh();

        EditorGUILayout.Space(2);

        using (new EditorGUI.DisabledScope(setup.targetAsset == null))
        {
            if (GUILayout.Button("Save to asset", GUILayout.Height(30)))
                setup.SaveToAsset();
        }

        if (setup.targetAsset == null)
            EditorGUILayout.HelpBox(
                "Place an item under the container and press Refresh, or assign a PickupItemData above.",
                MessageType.Info);
    }
}
