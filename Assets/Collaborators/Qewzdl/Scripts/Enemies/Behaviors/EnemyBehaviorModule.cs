using System;
using System.Collections.Generic;
using UnityEngine;

// A behaviour you can plug into an enemy.
//
// The brain used to name its nine states in a hardcoded list, so an enemy could
// only ever have exactly those nine and a new behaviour meant editing the file
// every enemy shares. A module inverts that: the config carries a list, the
// brain installs whatever is in it, and adding a behaviour is writing a class
// and dragging an asset - not touching anything the other enemies read.
//
// A ScriptableObject, so one asset serves every enemy that uses it and the
// tuning lives beside the other ten profiles rather than inside a prefab.
//
// ---------------------------------------------------------------------------
//
// The consequence of being an asset is the thing to remember when writing one:
// the module is SHARED. Two enemies configured with the same module are looking
// at the same object, and a field written during play is written for both. So a
// module holds settings and nothing else, and hands out per-enemy runtime
// objects through Install. That is why Install takes the installer rather than
// the module holding a reference to the brain.
//
// Modules come in two kinds and the interface deliberately does not distinguish
// them, because several are both:
//
//   A STATE is something the enemy does instead of everything else - patrolling,
//   searching, lying in wait. It registers a handler, and the brain's state
//   machine drives it.
//
//   A CAPABILITY is something a state uses while doing its job - checking a
//   hiding place, opening a door. It registers an object that states look up by
//   type, and a state that cannot find it does without.
//
// The split matters because the first six behaviours anybody asked for were
// three of each, and a system that only did states would have left half of them
// with nowhere to go.
public abstract class EnemyBehaviorModule : ScriptableObject
{
    [Tooltip(
        "Optional note for whoever opens the enemy config and wonders what " +
        "this one is for. Not used at runtime.")]
    [SerializeField, TextArea(2, 4)] private string notes;

    public string Notes => notes;

    // Called once per enemy, while the brain is being built. Register whatever
    // this behaviour contributes; register nothing and the module is inert,
    // which is a legitimate thing for a half-finished one to be.
    public abstract void Install(EnemyBehaviorInstaller installer);
}

// What a module is allowed to do while installing, and the only channel it has
// to the enemy being built.
//
// Narrow on purpose. A module gets the context because its handlers need it,
// and two ways to contribute; it does not get the brain, the state dictionary
// or the ability to ask what else is installed. Modules that could inspect each
// other would start depending on install order, and install order is a list in
// an inspector that somebody will reorder without knowing they were allowed to.
public sealed class EnemyBehaviorInstaller
{
    private readonly Dictionary<EnemyState, IEnemyStateHandler> stateHandlers;
    private readonly EnemyBehaviorCapabilities capabilities;

    public EnemyBrainContext Context { get; }

    public EnemyBehaviorInstaller(
        EnemyBrainContext context,
        Dictionary<EnemyState, IEnemyStateHandler> stateHandlers,
        EnemyBehaviorCapabilities capabilities
    )
    {
        Context = context;
        this.stateHandlers = stateHandlers;
        this.capabilities = capabilities;
    }

    // Last writer wins, quietly. Two modules claiming the same state is a
    // misconfiguration, but the brain is built at spawn on a live server and
    // there is nothing useful to do about it there - the enemy that refuses to
    // exist is worse than the enemy whose patrolling came from the second of
    // two patrol modules. ProjectAssetValidationTests is where that argument
    // gets had, before anybody plays.
    public void AddState(IEnemyStateHandler handler)
    {
        if (handler == null)
        {
            return;
        }

        stateHandlers[handler.State] = handler;
    }

    // Registered under its own concrete type, which is what states look up by.
    public void AddCapability<T>(T capability) where T : class
    {
        capabilities.Add(capability);
    }
}

// What the installed modules can do, for the states that want to use them.
//
// Keyed by type rather than by name or id, so asking for a capability is a
// compile-time question and a state cannot ask for one that was never written.
// Absence is the normal case rather than an error: an enemy without the hiding
// place capability searches rooms and walks past boxes, which is a different
// enemy rather than a broken one.
public sealed class EnemyBehaviorCapabilities
{
    private readonly Dictionary<Type, object> capabilities = new();

    public void Add(object capability)
    {
        if (capability == null)
        {
            return;
        }

        capabilities[capability.GetType()] = capability;
    }

    public bool TryGet<T>(out T capability) where T : class
    {
        foreach (KeyValuePair<Type, object> entry in capabilities)
        {
            if (entry.Value is T match)
            {
                capability = match;
                return true;
            }
        }

        capability = null;
        return false;
    }

    public bool Has<T>() where T : class
    {
        return TryGet(out T _);
    }

    public void Clear()
    {
        capabilities.Clear();
    }
}
