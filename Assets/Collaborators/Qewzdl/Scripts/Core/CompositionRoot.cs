using UnityEngine;

public abstract class CompositionRoot : MonoBehaviour
{
    private bool isComposed;

    private void Awake()
    {
        ResolveReferences();
        ComposeOnce();
    }

    protected abstract void ResolveReferences();
    protected abstract void Compose();

    private void ComposeOnce()
    {
        if (isComposed)
            return;

        Compose();
        isComposed = true;
    }
}