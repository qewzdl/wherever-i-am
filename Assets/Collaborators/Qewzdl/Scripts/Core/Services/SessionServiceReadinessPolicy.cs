using System;
using System.Collections.Generic;

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

        Require<IChatReadService>(services, missingContracts);
        Require<IChatCommandService>(services, missingContracts);

        if (requirement == Requirement.Game)
            Require<IMatchCompletionService>(services, missingContracts);

        if (missingContracts.Count == 0)
        {
            error = string.Empty;
            return true;
        }

        error =
            $"{owner} is missing required dynamic Session contract(s): " +
            string.Join(", ", missingContracts) + ".";
        return false;
    }

    private static void Require<TContract>(
        IServiceResolver services,
        ICollection<string> missingContracts)
        where TContract : class
    {
        try
        {
            if (services.TryResolve(out TContract _))
                return;
        }
        catch (ObjectDisposedException)
        {
        }

        missingContracts.Add(typeof(TContract).Name);
    }
}
