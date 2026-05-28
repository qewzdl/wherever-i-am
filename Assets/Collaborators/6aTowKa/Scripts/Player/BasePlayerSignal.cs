using System;

public abstract class BasePlayerSignal
{
    public string DebugName;
    public abstract Delegate[] GetListeners();
}
