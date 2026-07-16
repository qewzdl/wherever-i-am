using System;
using System.Collections.Generic;

internal sealed class ServiceRegistrationTransaction : IDisposable
{
    private ServiceScope scope;
    private readonly List<long> registrationIds = new();

    internal ServiceRegistrationTransaction(ServiceScope owner)
    {
        scope = owner;
    }

    public bool IsCompleted => scope == null;

    public void Commit()
    {
        ServiceScope owner = scope;

        if (owner == null)
            return;

        owner.CommitTransaction(this);
        scope = null;
    }

    public void Rollback()
    {
        ServiceScope owner = scope;

        if (owner == null)
            return;

        scope = null;
        owner.RollbackTransaction(this, registrationIds);
    }

    public void Dispose()
    {
        Rollback();
    }

    internal void Track(long registrationId)
    {
        registrationIds.Add(registrationId);
    }

    internal void Detach()
    {
        scope = null;
    }
}
