using System;

internal interface ISessionServiceRegistry : IServiceResolver
{
    event Action ServicesChanged;
}
