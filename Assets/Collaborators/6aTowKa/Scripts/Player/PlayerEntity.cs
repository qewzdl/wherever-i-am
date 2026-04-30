using Unity.Netcode;
using UnityEngine;

public class PlayerEntity : NetworkBehaviour
{
    private PlayerInput playerInput;
    private PlayerController playerController;
    private PlayerNetwork playerNetwork;
    private PlayerAnimation playerAnimation;

    public override void OnNetworkSpawn()
    {
        //both   
        playerNetwork = GetComponent<PlayerNetwork>();
        playerAnimation = GetComponent<PlayerAnimation>();

        if (IsOwner) //local player
        {
            playerInput = GetComponent<PlayerInput>();
            playerController = GetComponent<PlayerController>();

            playerInput.OnMoveUpdated += playerController.SetDirection;
            playerInput.OnCrouchUpdated += playerController.UpdateIsCrouching;

            playerController.IsCrouchingUpdated += StartCrouchAnimation;
            playerController.IsCrouchingUpdated += UpdateNetworkIsCrouching;
        }
        else //server player
        {
            GetComponentInChildren<Camera>().enabled = false;
            GetComponentInChildren<CameraFollow>().enabled = false;
            GetComponentInChildren<MouseLook>().enabled = false;
            GetComponentInChildren<AudioListener>().enabled = false;

            GetComponent<PlayerInput>().enabled = false;
            GetComponent<PlayerController>().enabled = false;

            playerNetwork.PlayerIsCrouching.OnValueChanged += StartCrouchAnimation;
        }
    }

    public override void OnNetworkDespawn()
    {
        //both

        if (IsOwner) // local player
        {
            playerInput.OnMoveUpdated -= playerController.SetDirection;
            playerInput.OnCrouchUpdated -= playerController.UpdateIsCrouching;

            playerController.IsCrouchingUpdated -= StartCrouchAnimation;
            playerController.IsCrouchingUpdated -= UpdateNetworkIsCrouching;
        }
        else // server player
        {
            playerNetwork.PlayerIsCrouching.OnValueChanged -= StartCrouchAnimation;
        }
    }

    //Animation
    private void StartCrouchAnimation(bool isCrouching)
    {
        playerAnimation.SetupAnimation(isCrouching);
    }

    private void StartCrouchAnimation(bool oldIsCrouching, bool newIsCrouching)
    {
        if (oldIsCrouching == newIsCrouching) return;
        StartCrouchAnimation(newIsCrouching);
    }

    //Network
    private void UpdateNetworkIsCrouching(bool isCrouching)
    {
        playerNetwork.SetNetworkPlayerIsCrouchingRpc(isCrouching);
    }
}
