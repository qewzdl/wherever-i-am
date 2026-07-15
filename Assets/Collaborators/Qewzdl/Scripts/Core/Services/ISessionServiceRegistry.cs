using System;

public interface ISessionServiceRegistry : IServiceResolver
{
    event Action ServicesChanged;
}
