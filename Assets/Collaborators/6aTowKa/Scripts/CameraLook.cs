using UnityEngine;
using UnityEngine.InputSystem;

public class CameraLook : MonoBehaviour
{
    [SerializeField] private Transform playerTransform;
    [SerializeField] private float sensitivity = 100f;

    private float rotationX;
    private float rotationY;
    private Vector2 delta;

    private IPauseService pauseService;

    public void Construct(IPauseService pauseService)
    {
        this.pauseService = pauseService;
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        delta = context.ReadValue<Vector2>();
    }

    private void Update()
    {
        if (!CanLook())
            return;

        delta = delta * sensitivity / 500f;

        rotationX -= delta.y;
        rotationX = Mathf.Clamp(rotationX, -90f, 90f);

        rotationY += delta.x;

        transform.localRotation = Quaternion.Euler(rotationX, 0f, 0f);
        playerTransform.localRotation = Quaternion.Euler(0f, rotationY, 0f);
    }

    private bool CanLook()
    {
        if (pauseService != null && pauseService.IsPaused)
            return false;

        if (Mouse.current == null)
            return false;

        return Cursor.lockState == CursorLockMode.Locked;
    }
}