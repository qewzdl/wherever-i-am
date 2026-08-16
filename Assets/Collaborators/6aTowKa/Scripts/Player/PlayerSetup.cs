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
        NetworkManager networkManager = NetworkManager;
        bool isMultiplayer = networkManager != null && networkManager.IsListening;
        bool isLocalControl = !isMultiplayer || IsOwner;

        NetworkObject networkObject = RequireComponentOnPlayer<NetworkObject>();
        NetworkTransform networkTransform = RequireComponentOnPlayer<NetworkTransform>();
        PlayerNetwork playerNetwork = RequireComponentOnPlayer<PlayerNetwork>();
        RequireComponentOnPlayer<PlayerActionGate>();
        PlayerHidingController playerHiding =
            RequireComponentOnPlayer<PlayerHidingController>();
        PlayerPostureController playerPosture = RequireComponentOnPlayer<PlayerPostureController>();
        PlayerInput playerInput = RequireComponentOnPlayer<PlayerInput>();
        PlayerInputHandler playerInputHandler = RequireComponentOnPlayer<PlayerInputHandler>();
        PlayerController playerController = RequireComponentOnPlayer<PlayerController>();
        PlayerInteraction playerInteraction = RequireComponentOnPlayer<PlayerInteraction>();
        PlayerUI playerUI = RequireComponentOnPlayer<PlayerUI>();
        PlayerOrchestrator playerOrchestrator = RequireComponentOnPlayer<PlayerOrchestrator>();

        CameraLook cameraLook = RequireComponentInPlayerChildren<CameraLook>();
        // Every camera, not just the first one found: the viewmodel overlay is
        // one too, and a player nobody controls must render through none of them.
        Camera[] playerCameras = RequireComponentsInPlayerChildren<Camera>();
        AudioListener audioListener = RequireComponentInPlayerChildren<AudioListener>();
        CapsuleCollider bodyCollider = RequireComponentInPlayerChildren<CapsuleCollider>();

        if (setupFailed)
        {
            enabled = false;
            return;
        }

        playerController.SetBodyCollider(bodyCollider);
        playerController.SetPlayerPosture(playerPosture);
        playerPosture.SetBodyCollider(bodyCollider);
        playerPosture.SetCameraPivot(cameraLook.transform);

        if (isMultiplayer)
        {
            networkObject.enabled = true;
            networkTransform.enabled = true;

            playerNetwork.enabled = true;
            playerHiding.enabled = true;
            playerPosture.enabled = true;
            playerInteraction.enabled = true;

            if (isLocalControl)
            {
                playerInput.enabled = true;
                playerInputHandler.enabled = true;
                playerController.enabled = true;
                playerUI.enabled = true;

                SetCamerasEnabled(playerCameras, true);
                audioListener.enabled = true;

                cameraLook.SetLocalControl(true);
            }
            else
            {
                AddDestroyingComponent(playerInput);
                AddDestroyingComponent(playerInputHandler);
                AddDestroyingComponent(playerController);
                AddDestroyingComponent(playerUI);
                SetCamerasEnabled(playerCameras, false);
                audioListener.enabled = false;

                cameraLook.SetLocalControl(false);
            }
        }
        else
        {
            AddDestroyingComponent(networkTransform);
            AddDestroyingComponent(playerNetwork);
            AddDestroyingComponent(playerHiding);

            playerInput.enabled = true;
            playerInputHandler.enabled = true;
            playerController.enabled = true;
            playerPosture.enabled = true;
            playerInteraction.enabled = true;
            playerUI.enabled = true;

            SetCamerasEnabled(playerCameras, true);
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

    private T[] RequireComponentsInPlayerChildren<T>() where T : Component
    {
        T[] components = GetComponentsInChildren<T>(true);

        if (components.Length == 0)
        {
            Debug.LogError($"{nameof(PlayerSetup)} requires {typeof(T).Name} in player children.", this);
            setupFailed = true;
        }

        return components;
    }

    private static void SetCamerasEnabled(Camera[] cameras, bool value)
    {
        for (int i = 0; i < cameras.Length; i++)
            cameras[i].enabled = value;
    }

    private void AddDestroyingComponent(Component component)
    {
        if (component == null)
            return;

        destroyingComponents.Add(component);
    }
}
