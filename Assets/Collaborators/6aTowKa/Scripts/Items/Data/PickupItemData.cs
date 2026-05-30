using UnityEngine;

[CreateAssetMenu(fileName = "NewInteractableObjectData", menuName = "Wherever I Am/Items/Item data")]
public class PickupItemData : DraggableObjectData
{
    public int ItemID;
    public string ItemViewModelDataName;
}
