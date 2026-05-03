using Unity.Netcode;
using UnityEngine;

public class PlayerEntity : MonoBehaviour
{
    private PlayerInput playerInput;
    private PlayerController playerController;
    private PlayerNetwork playerNetwork;
    private PlayerAnimation playerAnimation;

    private void Awake()
    {
        playerNetwork = GetComponent<PlayerNetwork>();
        playerAnimation = GetComponent<PlayerAnimation>();
        playerInput = GetComponent<PlayerInput>();
        playerController = GetComponent<PlayerController>();

        playerNetwork.Start();
            GetComponentInChildren<Camera>().enabled = false;
            GetComponentInChildren<CameraFollow>().enabled = false;
            GetComponentInChildren<MouseLook>().enabled = false;
            GetComponentInChildren<AudioListener>().enabled = false;

            GetComponent<PlayerInput>().enabled = false;
            GetComponent<PlayerController>().enabled = false;
    }

    private void OnEnable()
    {
        playerInput.OnMoveUpdated += playerController.SetDirection;
        playerInput.OnCrouchUpdated += playerController.UpdateIsCrouching;

        playerController.IsCrouchingUpdated += StartCrouchAnimation;
        playerController.IsCrouchingUpdated += UpdateNetworkIsCrouching;

        playerNetwork.PlayerIsCrouching.OnValueChanged += StartCrouchAnimation;
    }

    private void OnDisable()
    {
        playerInput.OnMoveUpdated -= playerController.SetDirection;
        playerInput.OnCrouchUpdated -= playerController.UpdateIsCrouching;

        playerController.IsCrouchingUpdated -= StartCrouchAnimation;
        playerController.IsCrouchingUpdated -= UpdateNetworkIsCrouching;

        playerNetwork.PlayerIsCrouching.OnValueChanged -= StartCrouchAnimation;
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
