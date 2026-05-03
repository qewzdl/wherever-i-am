using UnityEngine;

public class PlayerAnimation : PlayerComponent, IPlayerSignalListener
{
    [SerializeField] private Transform playerModelTransform;
    [SerializeField] private float animationSpeed;
    [SerializeField] private float crouchHeight;
    [SerializeField] private float standHeight;

    private float targetHeight;
    private float lastHeight;
    private bool playCrouchAnimation;

    protected override void OnPostInit(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner)
    {
        signals.CrouchUpdateSignal.Listen(SetupAnimation);

        if (isMultiplayer && !isOwner)
        {
            signals.CrouchSyncSignal.Listen(SetupAnimation);
        }
    }

    public void Cleanup()
    {
        signals.CrouchUpdateSignal.Unlisten(SetupAnimation);
        signals.CrouchSyncSignal.Unlisten(SetupAnimation); // ???????
    }

    private void Update()
    {
        if (playCrouchAnimation)
        {
            CrouchAnimation();
        }
    }

    public void SetupAnimation(bool isCrouching)
    {
        if (isCrouching) targetHeight = crouchHeight;
        else targetHeight = standHeight;
        playCrouchAnimation = true;
    }

    private void CrouchAnimation()
    {
        lastHeight = playerModelTransform.localScale.y;

        float currentHeight = Mathf.Lerp(playerModelTransform.localScale.y, targetHeight, animationSpeed * Time.deltaTime);
        if (Mathf.Abs(playerModelTransform.localScale.y - targetHeight) < 0.01)
        {
            playerModelTransform.localScale = new Vector3(playerModelTransform.localScale.x, targetHeight, playerModelTransform.localScale.z);
            playCrouchAnimation = false;
        }

        playerModelTransform.localScale = new Vector3(playerModelTransform.localScale.x, currentHeight, playerModelTransform.localScale.z);
        playerModelTransform.position += new Vector3(0, (currentHeight - lastHeight), 0);
    }

}
