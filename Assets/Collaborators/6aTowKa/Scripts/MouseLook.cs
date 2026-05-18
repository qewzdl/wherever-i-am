using UnityEngine;
using UnityEngine.InputSystem;

public class MouseLook : MonoBehaviour, IPauseServiceConsumer
{
    [SerializeField] private Transform playerTransform;
    [SerializeField] private float sensitivity = 100f;

    private float rotationX;
    private float rotationY;

    private IPauseService pauseService;

    public void Construct(IPauseService pauseService)
    {
        this.pauseService = pauseService;
    }

    public void BindPauseService(IPauseService pauseService)
    {
        Construct(pauseService);
    }

    private void Update()
    {
        if (!CanLook())
            return;

        Look();
    }

    private bool CanLook()
    {
        if (pauseService != null && pauseService.IsPaused)
            return false;

        if (Mouse.current == null)
            return false;

        return Cursor.lockState == CursorLockMode.Locked;
    }

    private void Look()
    {
        Vector2 delta = Mouse.current.delta.value * sensitivity / 500f;

        rotationX -= delta.y;
        rotationX = Mathf.Clamp(rotationX, -90f, 90f);

        rotationY += delta.x;

        playerTransform.localRotation = Quaternion.Euler(0f, rotationY, 0f);
        transform.localRotation = Quaternion.Euler(rotationX, 0f, 0f);
    }
}
