using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerSetup : NetworkBehaviour
{
    private List<Component> destroingComponents = new List<Component>();

    private void Start()
    {
        SetupPlayer();
    }

    public void SetupPlayer()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        bool isMultiplayer = networkManager != null && networkManager.IsListening;

        if (isMultiplayer)
        {
            GetComponent<NetworkObject>().enabled = true;
            GetComponent<NetworkTransform>().enabled = true;

            GetComponent<PlayerNetwork>().enabled = true;
            GetComponent<PlayerAnimation>().enabled = true;

            if (IsOwner) // local player
            {
                GetComponent<PlayerInput>().enabled = true;
                GetComponent<PlayerInputHandler>().enabled = true;
                GetComponent<PlayerController>().enabled = true;
            }
            else // server player
            {
                destroingComponents.Add(GetComponent<PlayerInput>());
                destroingComponents.Add(GetComponent<PlayerInputHandler>());
                destroingComponents.Add(GetComponent<PlayerController>());

                GetComponentInChildren<MouseLook>().enabled = false;
                GetComponentInChildren<Camera>().enabled = false;
            }
        }
        else // singleplayer
        {
            destroingComponents.Add(GetComponent<NetworkObject>());
            destroingComponents.Add(GetComponent<NetworkTransform>());
            destroingComponents.Add(GetComponent<PlayerNetwork>());

            GetComponent<PlayerInput>().enabled = true;
            GetComponent<PlayerInputHandler>().enabled = true;
            GetComponent<PlayerController>().enabled = true;
            GetComponent<PlayerAnimation>().enabled = true;
        }

        foreach (Behaviour component in GetComponents<Behaviour>())
        {
            if (!destroingComponents.Contains(component) && component.enabled == false)
            {
                Debug.LogWarning("Component (" + component.ToString() + ") is not enabled. The Player Setup cannot initialize this module");
            }
        }

        foreach (Component component in destroingComponents)
        {
            Destroy(component);
        }

        GetComponent<PlayerOrchestrator>().Setup(isMultiplayer, isMultiplayer && IsOwner);

        Destroy(this);
    }
}
