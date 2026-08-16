using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps a scene camera rendering only while there is no local player camera:
/// before the player object spawns, and again after it is destroyed. Two
/// enabled cameras with the same depth render in an order Unity does not
/// promise, so the scene camera has to switch off instead of racing the
/// player's one for who draws last.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class FallbackCamera : MonoBehaviour
{
    private static readonly List<FallbackCamera> instances = new();
    private static Camera localPlayerCamera;

    private Camera fallbackCamera;
    private AudioListener fallbackListener;

    public static void SetLocalPlayerCamera(Camera camera)
    {
        if (camera == null)
            return;

        localPlayerCamera = camera;
        ApplyToAll();
    }

    public static void ClearLocalPlayerCamera(Camera camera)
    {
        // A player that never owned the view must not hand it back.
        if (camera != null && localPlayerCamera != camera)
            return;

        localPlayerCamera = null;
        ApplyToAll();
    }

    private void Awake()
    {
        fallbackCamera = GetComponent<Camera>();
        fallbackListener = GetComponent<AudioListener>();
    }

    private void OnEnable()
    {
        instances.Add(this);
        Apply();
    }

    private void OnDisable()
    {
        instances.Remove(this);
    }

    private void Apply()
    {
        bool shouldRender = localPlayerCamera == null;

        fallbackCamera.enabled = shouldRender;

        if (fallbackListener != null)
            fallbackListener.enabled = shouldRender;
    }

    private static void ApplyToAll()
    {
        for (int i = instances.Count - 1; i >= 0; i--)
        {
            FallbackCamera instance = instances[i];

            if (instance == null)
            {
                instances.RemoveAt(i);
                continue;
            }

            instance.Apply();
        }
    }
}
