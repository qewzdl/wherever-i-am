using System;
using UnityEngine;

public class PlayerSignal
{
    private Action listeners;
    private string debugName;

    public PlayerSignal() { }

    public PlayerSignal(string debugName)
    {
        this.debugName = debugName;
    }

    public void Trigger()
    {
        if (!string.IsNullOrEmpty(debugName))
        {
            Debug.Log($"Signal {debugName} triggered");
        }

        listeners?.Invoke();
    }

    public void Listen(Action callback) => listeners += callback;
    public void Unlisten(Action callback) => listeners -= callback;
}

public class PlayerSignal<T>
{
    private Action<T> listeners;
    private string debugName;

    public PlayerSignal() { }

    public PlayerSignal(string debugName)
    {
        this.debugName = debugName;
    }

    public void Trigger(T data)
    {
        if (!string.IsNullOrEmpty(debugName))
        {
            Debug.Log($"Signal {debugName} triggered with: {data}");
        }

        listeners?.Invoke(data);
    }

    public void Listen(Action<T> callback) => listeners += callback;
    public void Unlisten(Action<T> callback) => listeners -= callback;
}