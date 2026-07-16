internal interface IServiceRegistrar
{
    void Register<TContract>(TContract service)
        where TContract : class;
}
