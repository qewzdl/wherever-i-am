public interface ISceneServiceRegistrar
{
    void Register<TContract>(TContract service)
        where TContract : class;
}
