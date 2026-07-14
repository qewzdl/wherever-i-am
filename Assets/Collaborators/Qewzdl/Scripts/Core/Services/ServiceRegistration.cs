using System;

public sealed class ServiceRegistration : IDisposable
{
    private ServiceScope scope;
    private readonly long registrationId;

    internal ServiceRegistration(ServiceScope owner, long id, Type contractType)
    {
        scope = owner;
        registrationId = id;
        ContractType = contractType;
    }

    public Type ContractType { get; }
    public bool IsActive => scope != null && scope.IsRegistrationActive(registrationId);

    public void Dispose()
    {
        ServiceScope owner = scope;
        scope = null;
        owner?.Unregister(registrationId);
    }

    internal void Detach()
    {
        scope = null;
    }
}
