using UnityEngine;
using UnityEngine.InputSystem;

public class MouseLook : MonoBehaviour
{
    [SerializeField] private Transform playerTransform;
    [SerializeField] private float sensitivity = 100f;

    private float rotationX = 0;
    private float rotationY = 0;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    private void Update()
    {
        Look();
    }

    private void Look()
    {
        Vector2 delta = Mouse.current.delta.value * sensitivity/500;

        rotationX -= delta.y;
        rotationX = Mathf.Clamp(rotationX, -90, 90);

        rotationY += delta.x;

        playerTransform.localRotation = Quaternion.Euler(0, rotationY, 0);
        gameObject.transform.localRotation = Quaternion.Euler(rotationX, rotationY, 0);
    }
}
