/// <summary>
/// Read-only access to services owned by a runtime scope.
/// Resolver instances are bound to the Unity main thread and become permanently
/// unavailable when their owning scope starts disposing.
/// </summary>
public interface IServiceResolver
{
    /// <summary>
    /// Gets whether the owning scope is disposing or disposed.
    /// This property must be read from the Unity main thread.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Resolves a registered interface contract on the Unity main thread.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown for non-main-thread access or an unavailable contract.
    /// </exception>
    /// <exception cref="System.ObjectDisposedException">
    /// Thrown after the owning scope starts disposing.
    /// </exception>
    T Resolve<T>() where T : class;

    /// <summary>
    /// Attempts to resolve a registered interface contract on the Unity main thread.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown for non-main-thread access.
    /// </exception>
    /// <exception cref="System.ObjectDisposedException">
    /// Thrown after the owning scope starts disposing.
    /// </exception>
    bool TryResolve<T>(out T service) where T : class;
}
