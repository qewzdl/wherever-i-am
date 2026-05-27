using UnityEngine;

public class EntranceDoorHandle : Item
{
    [SerializeField] private int handleID;
    public int HandleID
    {
        get { return handleID; }
        private set { }
    }
    public override bool Action()
    {
        return false;
    }

    public void Use()
    {
        Drop();
        Destroy(gameObject);
    }
}
