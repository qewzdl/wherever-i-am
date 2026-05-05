using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerSignal : BasePlayerSignal
{
    private Action listeners;
    private string debugTriggerString;

    public PlayerSignal(List<object> list, string _debugName)
    {
        list.Add(this);
        DebugName = _debugName;
    }

    public PlayerSignal(List<object> list, string _debugName, string _debugTriggerString)
    {
        list.Add(this);
        DebugName = _debugName;
        debugTriggerString = _debugTriggerString;
    }

    public void Trigger()
    {
        if (!string.IsNullOrEmpty(debugTriggerString))
        {
            Debug.Log(debugTriggerString);
        }

        listeners?.Invoke();
    }

    public override Delegate[] GetListeners()
    {
        return listeners?.GetInvocationList() ?? null;
    }

    public void Listen(Action callback) => listeners += callback;
    public void Unlisten(Action callback) => listeners -= callback;
}

public class PlayerSignal<T> : BasePlayerSignal
{
    private Action<T> listeners;
    private string debugTriggerString;

    public PlayerSignal(List<object> list, string _debugName)
    {
        list.Add(this);
        DebugName = _debugName;
    }

    public PlayerSignal(List<object> list, string _debugName, string _debugTriggerString)
    {
        list.Add(this);
        DebugName = _debugName;
        debugTriggerString = _debugTriggerString;
    }

    public void Trigger(T data)
    {
        if (!string.IsNullOrEmpty(debugTriggerString))
        {
            Debug.Log(debugTriggerString + " | data " + data);
        }

        listeners?.Invoke(data);
    }

    public override Delegate[] GetListeners()
    {
        return listeners?.GetInvocationList() ?? null;
    }

    public void Listen(Action<T> callback) => listeners += callback;
    public void Unlisten(Action<T> callback) => listeners -= callback;
}