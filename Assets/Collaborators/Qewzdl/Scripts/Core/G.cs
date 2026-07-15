using System;
using System.Threading;
using UnityEngine;

public static class G
{
    private static readonly object SyncRoot = new();

    private static IServiceResolver resolver;
    private static long currentGeneration;
    private static long nextGeneration;

    public static bool IsReady
    {
        get
        {
            lock (SyncRoot)
                return IsResolverReady(resolver);
        }
    }

    public static T Resolve<T>() where T : class
    {
        ValidateContract<T>();
        IServiceResolver current = GetReadyResolver();
        return current.Resolve<T>();
    }

    public static bool TryResolve<T>(out T service) where T : class
    {
        ValidateContract<T>();
        IServiceResolver current;

        lock (SyncRoot)
        {
            current = resolver;

            if (!IsResolverReady(current))
            {
                service = null;
                return false;
            }
        }

        try
        {
            return current.TryResolve(out service);
        }
        catch (ObjectDisposedException)
        {
            service = null;
            return false;
        }
    }

    internal static GlobalServicePublication Publish(IServiceResolver services)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        if (services.IsDisposed)
        {
            throw new ObjectDisposedException(
                nameof(services),
                "Cannot publish a disposed Global service resolver.");
        }

        lock (SyncRoot)
        {
            if (currentGeneration != 0)
            {
                throw new InvalidOperationException(
                    "Global services are already published.");
            }

            long generation = GetNextGeneration();
            resolver = services;
            currentGeneration = generation;
            return new GlobalServicePublication(generation);
        }
    }

    internal static bool IsPublicationActive(long generation)
    {
        if (generation == 0)
            return false;

        lock (SyncRoot)
            return currentGeneration == generation;
    }

    internal static void Unpublish(long generation)
    {
        if (generation == 0)
            return;

        lock (SyncRoot)
        {
            if (currentGeneration != generation)
                return;

            resolver = null;
            currentGeneration = 0;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void ResetRuntimeState()
    {
        lock (SyncRoot)
        {
            resolver = null;
            currentGeneration = 0;
        }
    }

    private static IServiceResolver GetReadyResolver()
    {
        lock (SyncRoot)
        {
            if (IsResolverReady(resolver))
                return resolver;
        }

        throw new InvalidOperationException(
            "Global services are unavailable before ProjectContext is ready or after it is disposed.");
    }

    private static bool IsResolverReady(IServiceResolver services)
    {
        return services != null && !services.IsDisposed;
    }

    private static long GetNextGeneration()
    {
        unchecked
        {
            nextGeneration++;

            if (nextGeneration == 0)
                nextGeneration++;

            return nextGeneration;
        }
    }

    private static void ValidateContract<T>() where T : class
    {
        Type contractType = typeof(T);

        if (contractType.IsInterface)
            return;

        throw new ArgumentException(
            $"Global service contract '{contractType.Name}' must be an interface.",
            nameof(T));
    }
}

internal sealed class GlobalServicePublication : IDisposable
{
    private long generation;

    internal GlobalServicePublication(long publicationGeneration)
    {
        if (publicationGeneration == 0)
            throw new ArgumentOutOfRangeException(nameof(publicationGeneration));

        generation = publicationGeneration;
    }

    internal bool IsActive
    {
        get
        {
            long publicationGeneration = Interlocked.Read(ref generation);
            return G.IsPublicationActive(publicationGeneration);
        }
    }

    public void Dispose()
    {
        long publicationGeneration = Interlocked.Exchange(ref generation, 0);
        G.Unpublish(publicationGeneration);
    }
}
