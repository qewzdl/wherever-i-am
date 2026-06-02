using System;
using UnityEngine;

public abstract class RuntimeDebugPanelSource : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private bool visible = true;
    [SerializeField] private int order;

    public event Action Changed;

    public bool IsVisible => visible && enabled && gameObject.activeInHierarchy;
    public int Order => order;
    public abstract string PanelTitle { get; }

    public bool IsValidSource(out string error)
    {
        return ValidateSource(out error);
    }

    public void AppendTo(RuntimeDebugTextBuilder builder)
    {
        if (!IsVisible)
        {
            return;
        }

        builder.Section(PanelTitle);
        BuildPanel(builder);
    }

    protected void RequestRefresh()
    {
        Changed?.Invoke();
    }

    protected abstract bool ValidateSource(out string error);

    protected abstract void BuildPanel(RuntimeDebugTextBuilder builder);
}