using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(ViewmodelItemEntry))]
public class ViewmodelItemEntryDrawer : PropertyDrawer
{
    private const float Spacing = 2f;

    private static readonly string[] FieldNames = { "position", "rotation", "scale" };
    private static readonly string[] Labels     = { "Position", "Rotation", "Scale" };

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = 0f;
        foreach (var name in FieldNames)
        {
            var p = property.FindPropertyRelative(name);
            height += EditorGUI.GetPropertyHeight(p, true) + Spacing;
        }
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        float y = position.y;
        for (int i = 0; i < FieldNames.Length; i++)
        {
            var p = property.FindPropertyRelative(FieldNames[i]);
            float h = EditorGUI.GetPropertyHeight(p, true);
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, h), p, new GUIContent(Labels[i]), true);
            y += h + Spacing;
        }

        EditorGUI.EndProperty();
    }
}
