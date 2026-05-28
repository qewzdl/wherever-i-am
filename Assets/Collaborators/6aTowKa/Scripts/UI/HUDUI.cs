using UnityEngine;

public class HUDUI : MonoBehaviour
{
    [SerializeField] private GameObject root;

    public void ShowHUD()
    {
        if (root != null)
            root.SetActive(true);
    }

    public void HideHUD()
    {
        if (root != null)
            root.SetActive(false);
    }
}
