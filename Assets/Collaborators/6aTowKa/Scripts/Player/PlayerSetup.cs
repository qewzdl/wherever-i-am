using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerSetup : NetworkBehaviour
{
    private readonly List<Component> destroyingComponents = new();

    private bool setupFailed;

    private void Start()
    {
        SetupPlayer();
    }

    public void SetupPlayer()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        bool isMultiplayer = networkManager != null && networkManager.IsListening;
        bool isLocalControl = !isMultiplayer || IsOwner;

        NetworkObject networkObject = RequireComponentOnPlayer<NetworkObject>();
        NetworkTransform networkTransform = RequireComponentOnPlayer<NetworkTransform>();
        PlayerNetwork playerNetwork = RequireComponentOnPlayer<PlayerNetwork>();
        PlayerAnimation playerAnimation = RequireComponentOnPlayer<PlayerAnimation>();
        PlayerInput playerInput = RequireComponentOnPlayer<PlayerInput>();
        PlayerInputHandler playerInputHandler = RequireComponentOnPlayer<PlayerInputHandler>();
        PlayerController playerController = RequireComponentOnPlayer<PlayerController>();
        PlayerInteraction playerInteraction = RequireComponentOnPlayer<PlayerInteraction>();
        PlayerUI playerUI = RequireComponentOnPlayer<PlayerUI>();
        PlayerOrchestrator playerOrchestrator = RequireComponentOnPlayer<PlayerOrchestrator>();

        CameraLook cameraLook = RequireComponentInPlayerChildren<CameraLook>();
        CameraFollow cameraFollow = RequireComponentInPlayerChildren<CameraFollow>();
        Camera playerCamera = RequireComponentInPlayerChildren<Camera>();
        AudioListener audioListener = RequireComponentInPlayerChildren<AudioListener>();

        if (setupFailed)
        {
            enabled = false;
            return;
        }

        if (isMultiplayer)
        {
            networkObject.enabled = true;
            networkTransform.enabled = true;

            playerNetwork.enabled = true;
            playerAnimation.enabled = true;

            if (isLocalControl)
            {
                playerInput.enabled = true;
                playerInputHandler.enabled = true;
                playerController.enabled = true;
                playerInteraction.enabled = true;
                playerUI.enabled = true;

                cameraFollow.enabled = true;
                playerCamera.enabled = true;
                audioListener.enabled = true;
                cameraLook.SetLocalControl(true);
            }
            else
            {
                AddDestroyingComponent(playerInput);
                AddDestroyingComponent(playerInputHandler);
                AddDestroyingComponent(playerController);
                AddDestroyingComponent(playerInteraction);
                AddDestroyingComponent(playerUI);
                AddDestroyingComponent(cameraFollow);

                playerCamera.enabled = false;
                audioListener.enabled = false;
                cameraLook.SetLocalControl(false);
            }
        }
        else
        {
            AddDestroyingComponent(networkObject);
            AddDestroyingComponent(networkTransform);
            AddDestroyingComponent(playerNetwork);

            playerInput.enabled = true;
            playerInputHandler.enabled = true;
            playerController.enabled = true;
            playerAnimation.enabled = true;
            playerInteraction.enabled = true;
            playerUI.enabled = true;

            cameraFollow.enabled = true;
            playerCamera.enabled = true;
            audioListener.enabled = true;
            cameraLook.SetLocalControl(true);
        }

        foreach (Behaviour component in GetComponents<Behaviour>())
        {
            if (!destroyingComponents.Contains(component) && component.enabled == false)
                Debug.LogWarning("Component (" + component + ") is not enabled. The Player Setup cannot initialize this module", this);
        }

        foreach (Component component in destroyingComponents)
            Destroy(component);

        playerOrchestrator.Setup(isMultiplayer, isLocalControl);

        Destroy(this);
    }

    private T RequireComponentOnPlayer<T>() where T : Component
    {
        T component = GetComponent<T>();

        if (component == null)
        {
            Debug.LogError($"{nameof(PlayerSetup)} requires {typeof(T).Name} on player root.", this);
            setupFailed = true;
        }

        return component;
    }

    private T RequireComponentInPlayerChildren<T>() where T : Component
    {
        T component = GetComponentInChildren<T>(true);

        if (component == null)
        {
            Debug.LogError($"{nameof(PlayerSetup)} requires {typeof(T).Name} in player children.", this);
            setupFailed = true;
        }

        return component;
    }

    private void AddDestroyingComponent(Component component)
    {
        if (component == null)
            return;

        destroyingComponents.Add(component);
    }
}