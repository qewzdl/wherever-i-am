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

        if (NetworkManager.Singleton) // Multiplayer
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
                GetComponent<PlayerInteraction>().enabled = true;
                GetComponent<PlayerUI>().enabled = true;
            }
            else // server player
            {
                destroingComponents.Add(GetComponent<PlayerInput>());
                destroingComponents.Add(GetComponent<PlayerInputHandler>());
                destroingComponents.Add(GetComponent<PlayerController>());
                destroingComponents.Add(GetComponent<PlayerInteraction>());
                destroingComponents.Add(GetComponent<PlayerUI>());

                destroingComponents.Add(GetComponentInChildren<MouseLook>());
                destroingComponents.Add(GetComponentInChildren<AudioListener>());
                destroingComponents.Add(GetComponentInChildren<CameraFollow>());

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
            GetComponent<PlayerInteraction>().enabled = true;
            GetComponent<PlayerUI>().enabled = true;
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

        GetComponent<PlayerOrchestrator>().Setup(NetworkManager.Singleton, IsOwner);

        Destroy(this);
    }
}
