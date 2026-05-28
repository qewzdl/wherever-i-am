using UnityEngine;
using UnityEngine.InputSystem;

public class CameraLook : MonoBehaviour
{
    [SerializeField] private Transform playerTransform;
    [SerializeField] private float sensitivity = 100f;

    private float rotationX;
    private float rotationY;
    private Vector2 delta;

    private Rigidbody playerRigidbody;
    private IPauseService pauseService;

    private void Awake()
    {
        rotationX = Mathf.Clamp(Mathf.DeltaAngle(0f, transform.localEulerAngles.x), -90f, 90f);

        if (playerTransform == null)
            return;

        rotationY = Mathf.DeltaAngle(0f, playerTransform.localEulerAngles.y);
        playerRigidbody = playerTransform.GetComponent<Rigidbody>();

        if (playerRigidbody == null)
            return;

        playerRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        playerRigidbody.constraints = (playerRigidbody.constraints & ~RigidbodyConstraints.FreezeRotationY)
            | RigidbodyConstraints.FreezeRotationX
            | RigidbodyConstraints.FreezeRotationZ;
    }

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
        if (CanLook())
        {
            Vector2 scaledDelta = delta * sensitivity / 500f;

            rotationX -= scaledDelta.y;
            rotationX = Mathf.Clamp(rotationX, -90f, 90f);

            rotationY += scaledDelta.x;
        }

        delta = Vector2.zero;
        ApplyCameraRotation();
    }

    private void ApplyCameraRotation()
    {
        if (playerRigidbody != null && playerTransform != null)
        {
            float yawOffset = Mathf.DeltaAngle(playerTransform.eulerAngles.y, rotationY);
            transform.localRotation = Quaternion.Euler(rotationX, yawOffset, 0f);
        }
        else if (playerTransform != null)
        {
            transform.localRotation = Quaternion.Euler(rotationX, 0f, 0f);
            playerTransform.localRotation = Quaternion.Euler(0f, rotationY, 0f);
        }
        else
        {
            transform.localRotation = Quaternion.Euler(rotationX, 0f, 0f);
        }
    }

    private void FixedUpdate()
    {
        if (playerRigidbody == null || !CanLook())
            return;

        Quaternion targetRotation = Quaternion.Euler(0f, rotationY, 0f);
        playerRigidbody.MoveRotation(targetRotation);
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
