using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    public Action<bool> IsCrouchingUpdated;

    [SerializeField] private float speed;
    [SerializeField] private float speedToCrouch;

    private Vector2 direction;
    private bool isCrouching = false;

    private void Update()
    {
        Move();
    }

    private void Move()
    {
        gameObject.transform.Translate(new Vector3(direction.x, 0, direction.y) * speed * Time.deltaTime);
    }

    public void SetDirection(Vector2 value)
    {
        direction = value;
    }

    public void UpdateIsCrouching()
    {
        isCrouching = !isCrouching;
        IsCrouchingUpdated?.Invoke(isCrouching);
    }
}
