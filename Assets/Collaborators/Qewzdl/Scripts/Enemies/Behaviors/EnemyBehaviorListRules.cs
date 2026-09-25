using System.Collections.Generic;
using System.Linq;

// What makes a list of behaviour modules add up to an enemy, asked of a config
// without building one.
//
// Written down once because two things ask. ProjectAssetValidationTests fails
// the build on any of these; EnemySetupWindow shows them while somebody is
// ticking modules on and off. The rules used to live inside the test, and a
// second copy in the window would have drifted from the first the day anybody
// added a rule to one of them - which is the one way a check can go wrong
// without anybody noticing.
public static class EnemyBehaviorListRules
{
    public enum ModuleKind
    {
        // Installs a state the enemy can be in.
        State,

        // Installs something states look up by type.
        Capability,

        // Installs nothing either of those can see: it reaches for a component
        // on the enemy - the navigator, the detector - and switches something
        // on there.
        Component,
    }

    // What one module contributes, found by installing it into a throwaway
    // enemy and looking at what arrived. Read off the result rather than off
    // the class, because what a module installs is code rather than a field,
    // and a list of names would go stale the first time somebody wrote one.
    public static ModuleKind KindOf(EnemyBehaviorModule module)
    {
        Dictionary<EnemyState, IEnemyStateHandler> handlers = new();
        EnemyBehaviorCapabilities capabilities = new();

        module.Install(CreateInstaller(null, handlers, capabilities));

        if (handlers.Count > 0)
        {
            return ModuleKind.State;
        }

        return capabilities.Count > 0
            ? ModuleKind.Capability
            : ModuleKind.Component;
    }

    // Everything wrong with a config's list, in words that say what the enemy
    // will do about it. Empty when the list adds up to an enemy.
    public static List<string> ProblemsWith(EnemyConfig config)
    {
        List<string> problems = new();

        if (config == null)
        {
            return problems;
        }

        IReadOnlyList<EnemyBehaviorModule> modules = config.BehaviorModules;

        if (modules == null || modules.Count == 0)
        {
            problems.Add(
                "lists no behaviours, so it builds an enemy that stands still " +
                "for the whole match");

            return problems;
        }

        if (modules.Any(module => module == null))
        {
            problems.Add("has an empty slot in its behaviour list");
        }

        // Installed rather than inspected, because what a module contributes
        // is code rather than a field, and a list of nothing but capability
        // modules reads as a full list right up until the enemy has nothing to
        // do with them.
        Dictionary<EnemyState, IEnemyStateHandler> handlers = new();
        EnemyBehaviorInstaller installer =
            CreateInstaller(config, handlers, new EnemyBehaviorCapabilities());

        foreach (EnemyBehaviorModule module in modules)
        {
            if (module != null)
            {
                module.Install(installer);
            }
        }

        if (handlers.Count == 0)
        {
            problems.Add(
                "lists only capabilities, so the enemy has nothing to do with " +
                "any of them");
        }

        // Read off the list by type rather than installed, unlike everything
        // above it.
        //
        // The senses install themselves into EnemyTargetDetector, a component
        // on the prefab, and there is no enemy here to hang one on - so
        // installing them would prove nothing either way. The question is worth
        // asking anyway, because the failure it catches is completely silent:
        // an enemy with neither sense never acquires a target, so chasing,
        // attacking and every stealth phase are installed and permanently
        // unreachable. She patrols forever and looks like she is working.
        //
        // A PatrolOnly enemy is meant to be exactly that, and its detector is
        // switched off wholesale, so it is not asked.
        bool hasASense = modules.Any(
            module => module is EnemySightModule or EnemyHearingModule);

        if (config.RequiresTargetDetector && !hasASense)
        {
            problems.Add(
                "gives the enemy neither sight nor hearing, so it can never " +
                "acquire a target and everything it would do with one is " +
                "unreachable");
        }

        // Dragging the same asset into the list twice is silent: the second
        // install of a state module replaces the first with an identical
        // handler, and the second install of a capability replaces it in a
        // registry keyed by type. Nothing misbehaves, and the list says
        // something its author did not mean.
        List<string> duplicates = modules
            .Where(module => module != null)
            .GroupBy(module => module.GetType().Name)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            problems.Add(
                $"lists {string.Join(" and ", duplicates)} more than once");
        }

        return problems;
    }

    // An enemy with a context and nothing else. The context has to exist - a
    // real one always does, and modules are written against that - but the
    // components hanging off it are absent, so modules that reach for them
    // quietly do nothing.
    private static EnemyBehaviorInstaller CreateInstaller(
        EnemyConfig config,
        Dictionary<EnemyState, IEnemyStateHandler> handlers,
        EnemyBehaviorCapabilities capabilities)
    {
        return new EnemyBehaviorInstaller(
            new EnemyBrainContext(
                config,
                null,
                null,
                null,
                null,
                null,
                new EnemyBlackboard(),
                null,
                null),
            handlers,
            capabilities);
    }
}
