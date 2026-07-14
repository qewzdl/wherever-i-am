public interface IServiceResolver
{
    bool IsDisposed { get; }

    T Resolve<T>() where T : class;
    bool TryResolve<T>(out T service) where T : class;
}
