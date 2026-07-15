using System;

internal sealed class TestServiceRegistrationPolicy : IServiceRegistrationPolicy
{
    internal static readonly TestServiceRegistrationPolicy Instance = new();

    private TestServiceRegistrationPolicy()
    {
    }

    public void ValidateRegistration(Type contractType, string scopeName)
    {
    }
}
