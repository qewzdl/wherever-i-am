using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private float speed;
    [SerializeField] private float speedToCrouch;

    private Vector2 direction;
    private float standHeight = 1;
    private float crouchHeight = 0.5f;

    private bool isCrouching = false;
    private bool playCrouchAnimation = false;

    #region Input
    private void OnMove(InputValue value)
    {
        direction = value.Get<Vector2>();
    }

    private void OnCrouch()
    {
        isCrouching = !isCrouching;
        playCrouchAnimation = true;
    }

    #endregion

    private void Update()
    {
        Move();

        if (playCrouchAnimation) Crouch();
    }

    private void Move()
    {
        gameObject.transform.Translate(new Vector3(direction.x, 0, direction.y) * speed * Time.deltaTime);
    }

    private void Crouch()
    {
        float targetHeight;
        float lastHeight = gameObject.transform.localScale.y;

        if (isCrouching) targetHeight = crouchHeight;
        else targetHeight = standHeight;

        float currentHeight = Mathf.Lerp(gameObject.transform.localScale.y, targetHeight, speedToCrouch * Time.deltaTime);
        if (Mathf.Abs(gameObject.transform.localScale.y - targetHeight) < 0.01)
        {
            gameObject.transform.localScale = new Vector3(gameObject.transform.localScale.x, targetHeight, gameObject.transform.localScale.z);
            playCrouchAnimation = false;
        }
        
        gameObject.transform.localScale = new Vector3(gameObject.transform.localScale.x, currentHeight, gameObject.transform.localScale.z);
        gameObject.transform.position += new Vector3(0, (currentHeight - lastHeight), 0);
    }
}
