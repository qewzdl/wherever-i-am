using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;

    private void Update()
    {
        gameObject.transform.position = target.position;
    }
}
