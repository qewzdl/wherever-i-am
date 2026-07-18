using UnityEngine;

internal sealed class PlayerHidingEffects
{
    private readonly Transform playerRoot;
    private readonly Rigidbody playerBody;

    private Collider[] playerColliders;
    private Renderer[] playerRenderers;
    private bool[] colliderEnabledStates;
    private bool[] rendererEnabledStates;

    private RigidbodyConstraints originalConstraints;
    private bool effectsApplied;

    internal PlayerHidingEffects(
        Transform playerRoot,
        Rigidbody playerBody
    )
    {
        this.playerRoot = playerRoot;
        this.playerBody = playerBody;
    }

    internal void Apply(
        bool hidePlayerVisuals,
        bool disablePlayerColliders
    )
    {
        Restore();
        Capture();

        if (disablePlayerColliders)
        {
            SetCollidersEnabled(false);
        }

        if (hidePlayerVisuals)
        {
            SetRenderersEnabled(false);
        }

        if (playerBody != null)
        {
            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
            playerBody.constraints = RigidbodyConstraints.FreezeAll;
        }

        effectsApplied = true;
    }

    internal void Restore()
    {
        if (!effectsApplied)
        {
            return;
        }

        RestoreColliderStates();
        RestoreRendererStates();

        if (playerBody != null)
        {
            playerBody.constraints = originalConstraints;
            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
        }

        effectsApplied = false;
    }

    private void Capture()
    {
        playerColliders = playerRoot != null
            ? playerRoot.GetComponentsInChildren<Collider>(true)
            : System.Array.Empty<Collider>();
        playerRenderers = playerRoot != null
            ? playerRoot.GetComponentsInChildren<Renderer>(true)
            : System.Array.Empty<Renderer>();

        colliderEnabledStates = new bool[playerColliders.Length];
        rendererEnabledStates = new bool[playerRenderers.Length];

        for (int i = 0; i < playerColliders.Length; i++)
        {
            Collider playerCollider = playerColliders[i];
            colliderEnabledStates[i] =
                playerCollider != null && playerCollider.enabled;
        }

        for (int i = 0; i < playerRenderers.Length; i++)
        {
            Renderer playerRenderer = playerRenderers[i];
            rendererEnabledStates[i] =
                playerRenderer != null && playerRenderer.enabled;
        }

        if (playerBody != null)
        {
            originalConstraints = playerBody.constraints;
        }
    }

    private void RestoreColliderStates()
    {
        if (playerColliders == null || colliderEnabledStates == null)
        {
            return;
        }

        int count = Mathf.Min(
            playerColliders.Length,
            colliderEnabledStates.Length
        );

        for (int i = 0; i < count; i++)
        {
            if (playerColliders[i] != null)
            {
                playerColliders[i].enabled = colliderEnabledStates[i];
            }
        }
    }

    private void RestoreRendererStates()
    {
        if (playerRenderers == null || rendererEnabledStates == null)
        {
            return;
        }

        int count = Mathf.Min(
            playerRenderers.Length,
            rendererEnabledStates.Length
        );

        for (int i = 0; i < count; i++)
        {
            if (playerRenderers[i] != null)
            {
                playerRenderers[i].enabled = rendererEnabledStates[i];
            }
        }
    }

    private void SetCollidersEnabled(bool value)
    {
        for (int i = 0; i < playerColliders.Length; i++)
        {
            if (playerColliders[i] != null)
            {
                playerColliders[i].enabled = value;
            }
        }
    }

    private void SetRenderersEnabled(bool value)
    {
        for (int i = 0; i < playerRenderers.Length; i++)
        {
            if (playerRenderers[i] != null)
            {
                playerRenderers[i].enabled = value;
            }
        }
    }
}
