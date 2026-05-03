using UnityEngine;

public class PlayerOrchestrator : MonoBehaviour
{
    public void Setup(bool isMultiplayer, bool isOwner)
    {
        PlayerInputHandler playerInputHandler = GetComponent<PlayerInputHandler>();
        PlayerController playerController = GetComponent<PlayerController>();
        PlayerNetwork playerNetwork = GetComponent<PlayerNetwork>();


        if (playerInputHandler != null)
        {
            playerInputHandler.OnMoveUpdated += playerController.SetDirection;
            playerInputHandler.OnCrouchUpdated += playerController.UpdateIsCrouching;
        }

        if (playerController != null)
        {
            playerController.IsCrouchingUpdated += StartCrouchAnimation;
            if (isMultiplayer)
            {
                playerController.IsCrouchingUpdated += UpdateNetworkIsCrouching;
            }
        }

        if (playerNetwork != null)
        {
            if (!isOwner)
            {
                playerNetwork.PlayerIsCrouching.OnValueChanged += StartCrouchAnimation;
            }
        }
    }

    private void Cleanup()
    {
        PlayerInputHandler playerInputHandler = GetComponent<PlayerInputHandler>();
        PlayerController playerController = GetComponent<PlayerController>();
        PlayerNetwork playerNetwork = GetComponent<PlayerNetwork>();


        if (playerInputHandler != null)
        {
            playerInputHandler.OnMoveUpdated -= playerController.SetDirection;
            playerInputHandler.OnCrouchUpdated -= playerController.UpdateIsCrouching;
        }

        if (playerController != null)
        {
            playerController.IsCrouchingUpdated -= StartCrouchAnimation;
            playerController.IsCrouchingUpdated -= UpdateNetworkIsCrouching;
        }

        if (playerNetwork != null)
        {
            playerNetwork.PlayerIsCrouching.OnValueChanged -= StartCrouchAnimation;
        }
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    //Animation
    private void StartCrouchAnimation(bool isCrouching)
    {
        GetComponent<PlayerAnimation>().SetupAnimation(isCrouching);
    }

    private void StartCrouchAnimation(bool oldIsCrouching, bool newIsCrouching)
    {
        if (oldIsCrouching == newIsCrouching) return;
        StartCrouchAnimation(newIsCrouching);
    }

    //Network
    private void UpdateNetworkIsCrouching(bool isCrouching)
    {
        GetComponent<PlayerNetwork>().SetNetworkPlayerIsCrouchingRpc(isCrouching);
    }
}
