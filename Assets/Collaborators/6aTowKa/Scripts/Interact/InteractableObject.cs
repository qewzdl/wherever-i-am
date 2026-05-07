using Unity.Netcode;
using UnityEngine;

public abstract class InteractableObject : NetworkBehaviour
{
    [SerializeField] public Sprite InteractionSprite;

#if UNITY_EDITOR
    // This runs in the Editor whenever you change something in the Inspector
    protected virtual void OnValidate()
    {
        if (!Application.isPlaying)
        {
            AssignInteractableLayer();
        }
    }

    private void AssignInteractableLayer()
    {
        int targetLayer = LayerMask.NameToLayer("Interactable");

        if (targetLayer != -1)
        {
            if (gameObject.layer != targetLayer)
            {
                gameObject.layer = targetLayer;
                UnityEditor.EditorUtility.SetDirty(this);
                Debug.Log($"[Editor] Automatically assigned 'Interactable' layer to {name}");
            }
        }
        else
        {
            Debug.LogWarning($"[Editor] Layer 'Interactable' not found! Please create it in Tags and Layers.");
        }
    }

#endif
    public abstract void Interact(InteractionContext context);
}