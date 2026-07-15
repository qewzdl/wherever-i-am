using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

internal static class SessionServiceReadinessPolicy
{
    private enum Requirement
    {
        None = 0,
        Lobby = 1,
        Game = 2
    }

    internal static bool Validate(
        ProjectSceneKind sceneKind,
        IServiceResolver services,
        out string error)
    {
        Requirement requirement = sceneKind switch
        {
            ProjectSceneKind.Lobby => Requirement.Lobby,
            ProjectSceneKind.Game => Requirement.Game,
            _ => Requirement.None
        };

        return Validate(
            requirement,
            $"Scene '{sceneKind}'",
            services,
            out error);
    }

    internal static bool Validate(
        GameState state,
        IServiceResolver services,
        out string error)
    {
        Requirement requirement = state switch
        {
            GameState.Lobby => Requirement.Lobby,
            GameState.LoadingGame => Requirement.Lobby,
            GameState.InGame => Requirement.Game,
            _ => Requirement.None
        };

        return Validate(
            requirement,
            $"Game state '{state}'",
            services,
            out error);
    }

    internal static bool ValidateServerPhase(
        ProjectSceneKind expectedScene,
        IServiceResolver services,
        out string error)
    {
        if (expectedScene != ProjectSceneKind.Lobby &&
            expectedScene != ProjectSceneKind.Game)
        {
            error = string.Empty;
            return true;
        }

        if (services == null || services.IsDisposed ||
            !services.TryResolve(out ISessionPhaseService phaseService))
        {
            error =
                $"Scene '{expectedScene}' requires {nameof(ISessionPhaseService)} " +
                "from the active Session scope.";
            return false;
        }

        if (!IsReady(phaseService))
        {
            error = $"{nameof(ISessionPhaseService)} is not ready.";
            return false;
        }

        if (phaseService.ServerScenePhase == expectedScene)
        {
            error = string.Empty;
            return true;
        }

        error =
            $"Server Session phase is '{phaseService.ServerScenePhase}', " +
            $"expected '{expectedScene}'.";
        return false;
    }

    private static bool Validate(
        Requirement requirement,
        string owner,
        IServiceResolver services,
        out string error)
    {
        if (requirement == Requirement.None)
        {
            error = string.Empty;
            return true;
        }

        if (services == null || services.IsDisposed)
        {
            error =
                $"{owner} requires an active Session resolver for dynamic " +
                "service readiness validation.";
            return false;
        }

        List<string> missingContracts = new();
        List<string> unreadyContracts = new();

        Require<IChatReadService>(services, missingContracts, unreadyContracts);
        Require<IChatCommandService>(services, missingContracts, unreadyContracts);

        if (requirement == Requirement.Game)
        {
            Require<IMatchCompletionService>(
                services,
                missingContracts,
                unreadyContracts);
        }

        if (missingContracts.Count == 0 && unreadyContracts.Count == 0)
        {
            error = string.Empty;
            return true;
        }

        List<string> failures = new();

        if (missingContracts.Count > 0)
        {
            failures.Add(
                "missing required dynamic Session contract(s): " +
                string.Join(", ", missingContracts));
        }

        if (unreadyContracts.Count > 0)
        {
            failures.Add(
                "has unready dynamic Session contract(s): " +
                string.Join(", ", unreadyContracts));
        }

        error = $"{owner} {string.Join("; ", failures)}.";
        return false;
    }

    private static void Require<TContract>(
        IServiceResolver services,
        ICollection<string> missingContracts,
        ICollection<string> unreadyContracts)
        where TContract : class
    {
        try
        {
            if (!services.TryResolve(out TContract service))
            {
                missingContracts.Add(typeof(TContract).Name);
                return;
            }

            if (IsReady(service))
                return;

            unreadyContracts.Add(typeof(TContract).Name);
            return;
        }
        catch (ObjectDisposedException)
        {
        }

        missingContracts.Add(typeof(TContract).Name);
    }

    private static bool IsReady<TContract>(TContract service)
        where TContract : class
    {
        if (service == null)
            return false;

        if (service is UnityEngine.Object unityObject && unityObject == null)
            return false;

        if (service is Behaviour behaviour && !behaviour.isActiveAndEnabled)
            return false;

        if (service is NetworkBehaviour networkBehaviour &&
            !networkBehaviour.IsSpawned)
        {
            return false;
        }

        return service is not ISessionServiceReadiness readiness ||
               readiness.IsSessionServiceReady;
    }
}
