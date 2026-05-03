using UnityEngine;

public class PlayerController : PlayerComponent, IPlayerSignalListener
{
    [SerializeField] private float speed;
    [SerializeField] private float speedToCrouch;

    private Vector2 direction;
    private bool isCrouching = false;

    protected override void OnPostInit(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner)
    {
        signals.MoveSignal.Listen(SetDirection);
        signals.CrouchInputSignal.Listen(UpdateIsCrouching);
    }

    public void Cleanup()
    {
        signals.MoveSignal.Unlisten(SetDirection);
        signals.CrouchInputSignal.Unlisten(UpdateIsCrouching);
    }

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
        signals.CrouchUpdateSignal.Trigger(isCrouching);
    }
}
