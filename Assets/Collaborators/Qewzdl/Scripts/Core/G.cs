using System;
using System.Threading;
using UnityEngine;

public static class G
{
    private static readonly object SyncRoot = new();

    private static IServiceResolver resolver;
    private static string activePublicationOwner;
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
        Type contractType = ValidateContract<T>();
        IServiceResolver current = GetReadyResolver(contractType);

        try
        {
            return current.Resolve<T>();
        }
        catch (ObjectDisposedException exception)
        {
            throw CreateUnavailableException(contractType, exception);
        }
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

    internal static GlobalServicePublication Publish(
        IServiceResolver services,
        string ownerDescription)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        if (string.IsNullOrWhiteSpace(ownerDescription))
        {
            throw new ArgumentException(
                "Global publication owner description cannot be empty.",
                nameof(ownerDescription));
        }

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
                    $"Global services are already published. Active owner: {activePublicationOwner}. " +
                    $"Requested owner: {ownerDescription}.{GetDiagnosticSuffixUnsafe()}");
            }

            long generation = GetNextGeneration();
            resolver = services;
            activePublicationOwner = ownerDescription;
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
            activePublicationOwner = null;
            currentGeneration = 0;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void ResetRuntimeState()
    {
        lock (SyncRoot)
        {
            resolver = null;
            activePublicationOwner = null;
            currentGeneration = 0;
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    internal static GlobalServiceDiagnostics Diagnostics
    {
        get
        {
            lock (SyncRoot)
            {
                return new GlobalServiceDiagnostics(
                    currentGeneration,
                    GetPublicationStateUnsafe(),
                    activePublicationOwner);
            }
        }
    }
#endif

    private static IServiceResolver GetReadyResolver(Type contractType)
    {
        lock (SyncRoot)
        {
            if (IsResolverReady(resolver))
                return resolver;

            throw CreateUnavailableExceptionUnsafe(contractType);
        }
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

    private static Type ValidateContract<T>() where T : class
    {
        Type contractType = typeof(T);

        if (!contractType.IsInterface)
        {
            throw new ArgumentException(
                $"Global service contract '{contractType.Name}' must be an interface.",
                nameof(T));
        }

        GlobalServiceContractPolicy.ValidatePublicAccess(contractType);
        return contractType;
    }

    private static InvalidOperationException CreateUnavailableException(
        Type contractType,
        Exception innerException)
    {
        lock (SyncRoot)
            return CreateUnavailableExceptionUnsafe(contractType, innerException);
    }

    private static InvalidOperationException CreateUnavailableExceptionUnsafe(
        Type contractType,
        Exception innerException = null)
    {
        string message =
            $"Global service contract '{contractType.Name}' is unavailable before " +
            $"ProjectContext is ready or after it is disposed.{GetDiagnosticSuffixUnsafe()}";

        return innerException == null
            ? new InvalidOperationException(message)
            : new InvalidOperationException(message, innerException);
    }

    private static string GetDiagnosticSuffixUnsafe()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return $" [generation={currentGeneration}, state={GetPublicationStateUnsafe()}]";
#else
        return string.Empty;
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private static GlobalServicePublicationState GetPublicationStateUnsafe()
    {
        if (currentGeneration == 0)
            return GlobalServicePublicationState.Unpublished;

        if (resolver == null)
            return GlobalServicePublicationState.Invalid;

        return resolver.IsDisposed
            ? GlobalServicePublicationState.ResolverDisposed
            : GlobalServicePublicationState.Ready;
    }
#endif
}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
internal enum GlobalServicePublicationState
{
    Unpublished = 0,
    Ready = 1,
    ResolverDisposed = 2,
    Invalid = 3
}

internal readonly struct GlobalServiceDiagnostics
{
    internal GlobalServiceDiagnostics(
        long generation,
        GlobalServicePublicationState state,
        string owner)
    {
        Generation = generation;
        State = state;
        Owner = owner;
    }

    internal long Generation { get; }
    internal GlobalServicePublicationState State { get; }
    internal string Owner { get; }
}
#endif

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
