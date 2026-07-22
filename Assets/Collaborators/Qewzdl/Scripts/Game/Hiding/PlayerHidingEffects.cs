using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed class PlayerHidingEffects
{
    private readonly Rigidbody playerBody;
    private readonly Transform visualRoot;
    private readonly Collider[] gameplayColliders;
    private readonly Collider[] hitboxColliders;
    private readonly Transform localViewmodelRoot;

    private Collider[] playerColliders;
    private Renderer[] playerRenderers;
    private bool[] colliderEnabledStates;
    private bool[] rendererEnabledStates;

    private RigidbodyConstraints originalConstraints;
    private bool effectsApplied;

    internal PlayerHidingEffects(
        Rigidbody playerBody,
        Transform visualRoot,
        Collider[] gameplayColliders,
        Collider[] hitboxColliders,
        Transform localViewmodelRoot
    )
    {
        this.playerBody = playerBody;
        this.visualRoot = visualRoot;
        this.gameplayColliders = gameplayColliders ??
                                 Array.Empty<Collider>();
        this.hitboxColliders = hitboxColliders ??
                               Array.Empty<Collider>();
        this.localViewmodelRoot = localViewmodelRoot;
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
        playerColliders = CollectExplicitColliders();
        playerRenderers = CollectExplicitRenderers();

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

    private Collider[] CollectExplicitColliders()
    {
        HashSet<Collider> unique = new();

        AddColliders(unique, gameplayColliders);
        AddColliders(unique, hitboxColliders);

        Collider[] result = new Collider[unique.Count];
        unique.CopyTo(result);
        return result;
    }

    private Renderer[] CollectExplicitRenderers()
    {
        HashSet<Renderer> unique = new();

        AddRenderers(unique, visualRoot);
        AddRenderers(unique, localViewmodelRoot);

        Renderer[] result = new Renderer[unique.Count];
        unique.CopyTo(result);
        return result;
    }

    private static void AddColliders(
        HashSet<Collider> destination,
        Collider[] source
    )
    {
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] != null)
            {
                destination.Add(source[i]);
            }
        }
    }

    private static void AddRenderers(
        HashSet<Renderer> destination,
        Transform root
    )
    {
        if (root == null)
        {
            return;
        }

        Renderer[] renderers =
            root.GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                destination.Add(renderers[i]);
            }
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
