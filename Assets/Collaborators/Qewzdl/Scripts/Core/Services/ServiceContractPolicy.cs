using System;
using System.Collections.Generic;

internal interface IServiceRegistrationPolicy
{
    void ValidateRegistration(Type contractType, string scopeName);
}

internal abstract class ServiceContractPolicy : IServiceRegistrationPolicy
{
    private readonly HashSet<Type> allowedContracts = new();
    private readonly string scopeKind;

    protected ServiceContractPolicy(string ownerScopeKind, params Type[] contracts)
    {
        if (string.IsNullOrWhiteSpace(ownerScopeKind))
            throw new ArgumentException("Scope kind cannot be empty.", nameof(ownerScopeKind));

        scopeKind = ownerScopeKind;

        if (contracts == null)
            throw new ArgumentNullException(nameof(contracts));

        for (int i = 0; i < contracts.Length; i++)
        {
            Type contractType = contracts[i] ??
                                throw new ArgumentException(
                                    "A service contract policy cannot contain a null contract.",
                                    nameof(contracts));

            if (!contractType.IsInterface)
            {
                throw new ArgumentException(
                    $"Service contract '{contractType.Name}' must be an interface.",
                    nameof(contracts));
            }

            if (!allowedContracts.Add(contractType))
            {
                throw new ArgumentException(
                    $"Service contract '{contractType.Name}' is duplicated in the {scopeKind} policy.",
                    nameof(contracts));
            }
        }
    }

    protected int ContractCount => allowedContracts.Count;

    protected bool Allows(Type contractType)
    {
        return contractType != null && allowedContracts.Contains(contractType);
    }

    public void ValidateRegistration(Type contractType, string scopeName)
    {
        if (Allows(contractType))
            return;

        string contractName = contractType != null
            ? contractType.Name
            : "Missing";

        throw new InvalidOperationException(
            $"Service contract '{contractName}' is not allowed in {scopeKind} scope '{scopeName}'.");
    }
}
