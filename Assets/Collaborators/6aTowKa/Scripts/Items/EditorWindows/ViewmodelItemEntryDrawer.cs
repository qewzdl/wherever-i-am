using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(ViewmodelItemEntry))]
public class ViewmodelItemEntryDrawer : PropertyDrawer
{
    private const float Spacing = 2f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return (EditorGUIUtility.singleLineHeight + Spacing) * 3;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        float lineH = EditorGUIUtility.singleLineHeight;
        float y = position.y;

        EditorGUI.PropertyField(
            new Rect(position.x, y, position.width, lineH),
            property.FindPropertyRelative("position"), new GUIContent("Position"));
        y += lineH + Spacing;

        EditorGUI.PropertyField(
            new Rect(position.x, y, position.width, lineH),
            property.FindPropertyRelative("rotation"), new GUIContent("Rotation"));
        y += lineH + Spacing;

        EditorGUI.PropertyField(
            new Rect(position.x, y, position.width, lineH),
            property.FindPropertyRelative("scale"), new GUIContent("Scale"));

        EditorGUI.EndProperty();
    }
}
