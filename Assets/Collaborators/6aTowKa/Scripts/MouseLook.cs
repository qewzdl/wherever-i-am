using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class MouseLook : MonoBehaviour
{
    [SerializeField] private Transform playerTransform;
    [SerializeField] private float sensitivity = 100f;

    private float rotationX = 0;
    private float rotationY = 0;

    private bool cursorIsLocked;

    private void OnApplicationFocus(bool focus)
    {
        cursorIsLocked = focus;
        SetCursorState(cursorIsLocked);
    }

    private void Start()
    {
        cursorIsLocked = true;
        SetCursorState(cursorIsLocked);
    }

    private void Update()
    {
        if (cursorIsLocked) Look();

        if (Keyboard.current.leftAltKey.wasPressedThisFrame)
        {
            cursorIsLocked = false;
            SetCursorState(cursorIsLocked);
        }
        else if (Keyboard.current.leftAltKey.wasReleasedThisFrame)
        {
            cursorIsLocked = true;
            SetCursorState(cursorIsLocked);
        }
    }

    private void Look()
    {
        Vector2 delta = Mouse.current.delta.value * sensitivity/500;

        rotationX -= delta.y;
        rotationX = Mathf.Clamp(rotationX, -90, 90);

        rotationY += delta.x;

        playerTransform.localRotation = Quaternion.Euler(0, rotationY, 0);
        gameObject.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);
    }

    private void SetCursorState(bool isLocked)
    {
        if (isLocked) Cursor.lockState = CursorLockMode.Locked;
        else Cursor.lockState = CursorLockMode.None;

        Cursor.visible = !isLocked;
    }
}
