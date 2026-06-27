using UnityEngine;

[CreateAssetMenu(fileName = "NewItemData", menuName = "Wherever I Am/Items/Item data")]
public class PickupItemData : DraggableObjectData
{
    public int ItemID;

    [Header("Viewmodel")]
    public ViewmodelItemEntry ViewmodelData = new ViewmodelItemEntry();
}
