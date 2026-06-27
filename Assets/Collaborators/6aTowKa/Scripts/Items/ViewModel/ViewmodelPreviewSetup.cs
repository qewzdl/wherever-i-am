using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Editor-only helper that lives on the ViewmodelContainer in the ViewmodelPreview scene.
/// Place an item prefab under the container, tune its model's transform, then write that
/// transform back into the item's <see cref="PickupItemData.ViewmodelData"/>.
/// </summary>
public class ViewmodelPreviewSetup : MonoBehaviour
{
    [SerializeField] public PickupItemData targetAsset;

#if UNITY_EDITOR
    // PickupItem.data / PickupItem.model are serialized but not publicly exposed, and those
    // runtime classes must not be modified — so we read them through SerializedObject.
    private const string DataFieldName  = "data";
    private const string ModelFieldName = "model";

    /// <summary>Pull the PickupItemData reference from the item placed under the container.</summary>
    public void Refresh()
    {
        var item = GetComponentInChildren<PickupItem>(true);
        if (item == null)
        {
            Debug.LogWarning("[ViewmodelPreview] No PickupItem found under the container.", this);
            return;
        }

        var data = new SerializedObject(item).FindProperty(DataFieldName)?.objectReferenceValue as PickupItemData;
        if (data == null)
        {
            Debug.LogWarning("[ViewmodelPreview] The item has no PickupItemData assigned.", this);
            return;
        }

        Undo.RecordObject(this, "Assign viewmodel target");
        targetAsset = data;
        EditorUtility.SetDirty(this);
        Debug.Log($"[ViewmodelPreview] Target set to '{data.name}'.", this);
    }

    /// <summary>Store the model's transform (relative to the container) into the asset.</summary>
    [ContextMenu("Save to asset")]
    public void SaveToAsset()
    {
        if (targetAsset == null)
        {
            Debug.LogError("[ViewmodelPreview] No target asset. Press Refresh first.", this);
            return;
        }

        var item = GetComponentInChildren<PickupItem>(true);
        if (item == null)
        {
            Debug.LogError("[ViewmodelPreview] No PickupItem found under the container.", this);
            return;
        }

        var model = new SerializedObject(item).FindProperty(ModelFieldName)?.objectReferenceValue as GameObject;
        if (model == null)
        {
            Debug.LogError("[ViewmodelPreview] The item's 'model' field is not assigned.", this);
            return;
        }

        // In game the model is instantiated as a *direct* child of the container, then
        // ViewmodelData is applied to its local transform. Here the model sits deeper in the
        // hierarchy, so express its transform relative to the container as if it were a direct
        // child — position and rotation are exact, scale is the closest TRS representation.
        Matrix4x4 relative = transform.worldToLocalMatrix * model.transform.localToWorldMatrix;

        Undo.RecordObject(targetAsset, "Save viewmodel transform");
        targetAsset.ViewmodelData.position = relative.GetColumn(3);
        targetAsset.ViewmodelData.rotation = relative.rotation.eulerAngles;
        targetAsset.ViewmodelData.scale    = relative.lossyScale;
        EditorUtility.SetDirty(targetAsset);
        AssetDatabase.SaveAssets();

        Debug.Log($"[ViewmodelPreview] Saved to '{targetAsset.name}': " +
                  $"pos={targetAsset.ViewmodelData.position}, " +
                  $"rot={targetAsset.ViewmodelData.rotation}, " +
                  $"scale={targetAsset.ViewmodelData.scale}", this);
    }
#endif
}
