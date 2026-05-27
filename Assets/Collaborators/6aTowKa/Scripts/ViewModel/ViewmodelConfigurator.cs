using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Вешается на контейнер (дочерний объект камеры).
/// Кинь предмет внутрь контейнера, настрой положение и нажми F (или кнопку в инспекторе).
/// Данные сохранятся в указанный ViewmodelItemData.
/// </summary>
public class ViewmodelConfigurator : MonoBehaviour
{
    [Header("База данных")]
    public ViewmodelItemData database;

    [Header("Клавиша сохранения")]
    public KeyCode saveKey = KeyCode.F;

    [Header("(Опционально) Имя вручную — если пусто, берётся имя дочернего объекта")]
    public string overrideName = "";

    private void Update()
    {
        if (Input.GetKeyDown(saveKey))
        {
            SaveCurrent();
        }
    }

    [ContextMenu("Сохранить текущий предмет")]
    public void SaveCurrent()
    {
        if (database == null)
        {
            Debug.LogError("[ViewmodelConfigurator] Не назначен ViewmodelItemData!");
            return;
        }

        if (transform.childCount == 0)
        {
            Debug.LogError("[ViewmodelConfigurator] Контейнер пустой — кинь предмет внутрь!");
            return;
        }

        Transform item = transform.GetChild(0);

        string itemName = string.IsNullOrEmpty(overrideName) ? item.name : overrideName;

        Vector3 pos = item.localPosition;
        Vector3 rot = item.localRotation.eulerAngles;
        Vector3 scale = item.localScale;

        database.SetEntry(itemName, pos, rot, scale);

        Debug.Log($"[ViewmodelConfigurator] Сохранено '{itemName}':\n" +
                  $"  Position: {pos}\n" +
                  $"  Rotation: {rot}\n" +
                  $"  Scale:    {scale}");
    }

#if UNITY_EDITOR
    // Рисуем кнопку прямо в инспекторе во время плея
    [CustomEditor(typeof(ViewmodelConfigurator))]
    public class ViewmodelConfiguratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            GUILayout.Space(8);

            var script = (ViewmodelConfigurator)target;

            GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
            if (GUILayout.Button("💾  Сохранить текущий предмет", GUILayout.Height(36)))
            {
                script.SaveCurrent();
            }
            GUI.backgroundColor = Color.white;

            if (Application.isPlaying)
            {
                GUILayout.Label($"Или нажми [{script.saveKey}] в игре", EditorStyles.centeredGreyMiniLabel);
            }
        }
    }
#endif
}
