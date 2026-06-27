using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ViewmodelItemData", menuName = "Viewmodel/Item Data")]
public class ViewModelsItemsData : ScriptableObject
{
    private static ViewModelsItemsData instance;
    public static ViewModelsItemsData Instance
    {
        get
        {
            if (instance == null)
            {
                instance = Resources.Load<ViewModelsItemsData>("ViewModelsData");
            }
            return instance;
        }
    }

    public List<ViewmodelItemEntry> items = new List<ViewmodelItemEntry>();

    // Найти запись по имени
    public ViewmodelItemEntry GetEntry(string itemName)
    {
        return items.Find(e => e.itemName == itemName);
    }

    // Добавить или обновить запись
    public void SetEntry(string itemName, Vector3 position, Vector3 rotation, Vector3 scale)
    {
        var entry = items.Find(e => e.itemName == itemName);
        if (entry == null)
        {
            entry = new ViewmodelItemEntry { itemName = itemName };
            items.Add(entry);
            Debug.Log($"[ViewmodelItemData] Добавлен новый предмет: {itemName}");
        }
        else
        {
            Debug.Log($"[ViewmodelItemData] Обновлён предмет: {itemName}");
        }

        entry.position = position;
        entry.rotation = rotation;
        entry.scale = scale;

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();
#endif
    }
}

[Serializable]
public class ViewmodelItemEntry
{
    public string itemName;
    public Vector3 position;
    public Vector3 rotation; // эйлеровы углы
    public Vector3 scale = Vector3.one;

    public void ApplyTo(Transform t)
    {
        t.localPosition = position;
        t.localRotation = Quaternion.Euler(rotation);
        t.localScale = scale;
    }

    public void SetFrom(Transform t)
    {
        position = t.localPosition;
        rotation = t.localRotation.eulerAngles;
        scale = t.localScale;
    }
}
