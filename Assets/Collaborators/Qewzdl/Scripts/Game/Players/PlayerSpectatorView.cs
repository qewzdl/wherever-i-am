using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// What a caught player sees: the match from a survivor's eyes, left mouse
// button for the next one.
//
// It puts its camera where that player's camera actually is, taken from the
// pose they publish themselves. Nothing here models crouching, hiding places
// or anything else that moves a camera - whatever the watched player's camera
// does, this follows, including whatever it learns to do later.
[DisallowMultipleComponent]
public sealed class PlayerSpectatorView : MonoBehaviour
{
    private const float ViewCatchUp = 30f;

    private Transform view;
    private PlayerEnemyAttackReceiver self;
    private PlayerEnemyAttackReceiver watched;
    private PlayerGazeNetwork watchedGaze;
    private Vector3 smoothedLocalPosition;
    private Quaternion smoothedLocalRotation = Quaternion.identity;
    private bool hasSmoothedPose;

    public PlayerEnemyAttackReceiver Watched => watched;

    internal static PlayerSpectatorView AttachTo(GameObject player)
    {
        if (player == null)
        {
            return null;
        }

        PlayerSpectatorView existing = player.GetComponent<PlayerSpectatorView>();
        return existing != null
            ? existing
            : player.AddComponent<PlayerSpectatorView>();
    }

    private void Awake()
    {
        self = GetComponent<PlayerEnemyAttackReceiver>();
        view = ResolveViewTransform(gameObject);

        if (view == null)
        {
            Debug.LogError(
                $"{nameof(PlayerSpectatorView)} found no camera to watch through.",
                this);

            enabled = false;
            return;
        }

        StopPlaying();
    }

    private void LateUpdate()
    {
        if (!IsWatchable(watched) || WasNextRequested())
        {
            Watch(NextTarget(PlayerEnemyAttackReceiver.All, self, watched));
        }

        // Whatever their camera is doing - crouched, folded into a hiding
        // place, or anything added to it later - arrives as one pose. Nothing
        // here needs to know which of those it is.
        if (watchedGaze == null ||
            !watchedGaze.TryGetLocalViewPose(
                out Vector3 localPosition,
                out Quaternion localRotation))
        {
            return;
        }

        // Poses land at the network tick and the view is drawn far more often,
        // so the camera's own movement is followed rather than snapped to. The
        // body underneath is not smoothed here at all: it is already
        // interpolated, and smoothing it again would leave the view trailing
        // behind a running player.
        if (hasSmoothedPose)
        {
            float catchUp = 1f - Mathf.Exp(-ViewCatchUp * Time.deltaTime);
            smoothedLocalPosition =
                Vector3.Lerp(smoothedLocalPosition, localPosition, catchUp);
            smoothedLocalRotation =
                Quaternion.Slerp(smoothedLocalRotation, localRotation, catchUp);
        }
        else
        {
            smoothedLocalPosition = localPosition;
            smoothedLocalRotation = localRotation;
            hasSmoothedPose = true;
        }

        Transform body = watchedGaze.transform;

        view.SetPositionAndRotation(
            body.TransformPoint(smoothedLocalPosition),
            body.rotation * smoothedLocalRotation);
    }

    // Cycles in registration order and wraps, skipping this player and anyone
    // already caught. Null when there is nobody left to watch.
    internal static PlayerEnemyAttackReceiver NextTarget(
        IReadOnlyList<PlayerEnemyAttackReceiver> players,
        PlayerEnemyAttackReceiver self,
        PlayerEnemyAttackReceiver current)
    {
        if (players == null || players.Count == 0)
        {
            return null;
        }

        int currentIndex = -1;

        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] == current)
            {
                currentIndex = i;
                break;
            }
        }

        for (int step = 1; step <= players.Count; step++)
        {
            PlayerEnemyAttackReceiver candidate =
                players[(currentIndex + step) % players.Count];

            if (candidate != null && candidate != self && !candidate.IsEliminated)
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool WasNextRequested()
    {
        Mouse mouse = Mouse.current;
        return mouse != null && mouse.leftButton.wasPressedThisFrame;
    }

    private bool IsWatchable(PlayerEnemyAttackReceiver player)
    {
        return player != null && player != self && !player.IsEliminated;
    }

    private void Watch(PlayerEnemyAttackReceiver player)
    {
        watched = player;
        watchedGaze = player != null
            ? player.GetComponent<PlayerGazeNetwork>()
            : null;

        // Switching player is a cut, not a glide between two heads.
        hasSmoothedPose = false;
    }

    // The camera under the look pivot, which is the one this player sees
    // through. The viewmodel overlay camera lives on its own branch and would
    // answer a plain search for a camera first.
    private static Transform ResolveViewTransform(GameObject player)
    {
        CameraLook cameraLook = player.GetComponentInChildren<CameraLook>(true);

        if (cameraLook != null)
        {
            Camera pivotCamera = cameraLook.GetComponentInChildren<Camera>(true);
            return pivotCamera != null ? pivotCamera.transform : cameraLook.transform;
        }

        Camera camera = player.GetComponentInChildren<Camera>(true);
        return camera != null ? camera.transform : null;
    }

    // Everything that let this player act on the world. The body itself is
    // taken out of play by whoever eliminated it.
    private void StopPlaying()
    {
        DisableIfPresent<CameraLook>();
        DisableIfPresent<PlayerController>();
        DisableIfPresent<PlayerInteraction>();
        DisableIfPresent<PlayerInputHandler>();
        DisableIfPresent<PlayerPostureController>();
        DisableIfPresent<PlayerUI>();
        DisableIfPresent<PlayerInput>();

        // CameraLook hands the cursor back when it is switched off, which is
        // right for a menu and wrong here: watching is still playing, and the
        // pause menu takes the cursor back on its own when it needs it.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void DisableIfPresent<T>() where T : Behaviour
    {
        T[] behaviours = GetComponentsInChildren<T>(true);

        for (int i = 0; i < behaviours.Length; i++)
        {
            behaviours[i].enabled = false;
        }
    }
}
