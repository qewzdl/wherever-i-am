using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

[Category("Gameplay")]
public sealed class ObjectiveBindingAndMapRootTests
{
    private readonly List<Object> cleanup = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = cleanup.Count - 1; i >= 0; i--)
        {
            if (cleanup[i] != null)
                Object.DestroyImmediate(cleanup[i]);
        }

        cleanup.Clear();
    }

    [Test]
    public void BindingRegistry_RejectsNullDuplicateAndMissingBindings()
    {
        ObjectiveDefinition door = CreateObjective("door", requiresSceneBinding: true);
        ObjectiveDefinition escape = CreateObjective("escape", requiresSceneBinding: true);
        ObjectiveSceneBindingRegistry registry = CreateRegistry(
            CreateBinding(door));
        ObjectiveSequenceDefinition sequence = CreateSequence(door, escape);

        Assert.That(registry.IsValidForSequence(sequence, out string error), Is.False);
        StringAssert.Contains("no scene binding for required objective 'escape'", error);

        registry.ConfigureEditor(new[] { CreateBinding(door), null });
        Assert.That(registry.IsValidForSequence(sequence, out error), Is.False);
        StringAssert.Contains("null binding at index 1", error);

        registry.ConfigureEditor(new[] { CreateBinding(door), CreateBinding(door) });
        Assert.That(registry.IsValidForSequence(sequence, out error), Is.False);
        StringAssert.Contains("two scene bindings for objective 'door'", error);

        registry.ConfigureEditor(new[] { CreateBinding(door), CreateBinding(escape) });
        Assert.That(registry.IsValidForSequence(sequence, out error), Is.True, error);

        Assert.That(registry.IsValidForSequence(null, out error), Is.False);
        StringAssert.Contains("null objective sequence", error);
    }

    [Test]
    public void BindingRegistry_DoesNotRequireBindingForObjectivesThatDeclareNone()
    {
        ObjectiveDefinition door = CreateObjective("door", requiresSceneBinding: true);
        ObjectiveDefinition timer = CreateObjective("timer", requiresSceneBinding: false);
        ObjectiveSceneBindingRegistry registry = CreateRegistry(CreateBinding(door));

        Assert.That(
            registry.IsValidForSequence(CreateSequence(door, timer), out string error),
            Is.True,
            error);
    }

    // A half-bound registry would leave live bindings pointing at a flow that
    // never finished starting, so a bad entry has to take the earlier ones back.
    [Test]
    public void BindingRegistry_BindAllRollsBackEverythingItAlreadyBound()
    {
        ObjectiveDefinition door = CreateObjective("door", requiresSceneBinding: true);
        ObjectiveSceneBinding bound = CreateBinding(door);
        ObjectiveSceneBindingRegistry registry = CreateRegistry(bound, null);
        NetworkObjectiveFlow flow = CreateObjectiveFlow();

        Assert.That(registry.TryBindAll(flow, out string error), Is.False);
        StringAssert.Contains("null binding at index 1", error);
        Assert.That(bound.IsBound, Is.False);

        registry.ConfigureEditor(new[] { bound });
        Assert.That(registry.TryBindAll(flow, out error), Is.True, error);
        Assert.That(bound.IsBound, Is.True);

        Assert.That(registry.TryBindAll(null, out error), Is.False);
        StringAssert.Contains("null objective flow", error);

        registry.UnbindAll();
        Assert.That(bound.IsBound, Is.False);
    }

    [Test]
    public void BindingRegistry_LooksUpBindingsByTheObjectiveAssetItself()
    {
        ObjectiveDefinition door = CreateObjective("door", requiresSceneBinding: true);
        ObjectiveSceneBinding binding = CreateBinding(door);
        ObjectiveSceneBindingRegistry registry = CreateRegistry(binding);

        Assert.That(registry.TryGetBinding(door, out ObjectiveSceneBinding found), Is.True);
        Assert.That(found, Is.SameAs(binding));

        // A different asset that happens to be named the same is a different
        // objective, which a name lookup could not tell apart.
        ObjectiveDefinition sameName = CreateObjective("door", requiresSceneBinding: true);
        Assert.That(registry.TryGetBinding(sameName, out _), Is.False);
        Assert.That(registry.TryGetBinding(null, out _), Is.False);

        binding.SetActiveState(true);
        registry.DeactivateAll();
        Assert.That(binding.IsActive, Is.False);
    }

    // Spawn points used to be handed out by clientId % spawnCount. Client ids
    // are not contiguous, so two connected players could land on one point.
    [Test]
    public void MapRoot_GivesEveryClientItsOwnSpawnPointAndKeepsIt()
    {
        GameMapRoot mapRoot = CreateMapRoot(spawnPointCount: 2);

        Assert.That(mapRoot.TryGetPlayerSpawn(1, out Vector3 first, out _), Is.True);
        Assert.That(mapRoot.TryGetPlayerSpawn(3, out Vector3 second, out _), Is.True);
        Assert.That(first, Is.Not.EqualTo(second));

        Assert.That(mapRoot.TryGetPlayerSpawn(1, out Vector3 firstAgain, out _), Is.True);
        Assert.That(firstAgain, Is.EqualTo(first));
    }

    [Test]
    public void MapRoot_FallsBackWhenThereAreMorePlayersThanSpawnPointsOrNone()
    {
        GameMapRoot mapRoot = CreateMapRoot(spawnPointCount: 2);

        Assert.That(mapRoot.TryGetPlayerSpawn(0, out _, out _), Is.True);
        Assert.That(mapRoot.TryGetPlayerSpawn(1, out _, out _), Is.True);
        Assert.That(
            mapRoot.TryGetPlayerSpawn(2, out Vector3 shared, out _),
            Is.True,
            "A third player still needs somewhere to stand.");
        Assert.That(mapRoot.TryGetPlayerSpawn(0, out Vector3 firstPoint, out _), Is.True);
        Assert.That(shared, Is.EqualTo(firstPoint));

        GameMapRoot emptyRoot = CreateMapRoot(spawnPointCount: 0);
        emptyRoot.transform.position = new Vector3(3f, 4f, 5f);

        Assert.That(
            emptyRoot.TryGetPlayerSpawn(0, out Vector3 fallback, out _),
            Is.False);
        Assert.That(fallback, Is.EqualTo(emptyRoot.transform.position));
    }

    private ObjectiveDefinition CreateObjective(string id, bool requiresSceneBinding)
    {
        ObjectiveDefinition objective =
            Track(ScriptableObject.CreateInstance<ObjectiveDefinition>());
        objective.name = id;
        TestReflection.SetField(objective, "requiredProgress", 1f);
        TestReflection.SetField(objective, "requiresSceneBinding", requiresSceneBinding);
        return objective;
    }

    private ObjectiveSequenceDefinition CreateSequence(params ObjectiveDefinition[] objectives)
    {
        ObjectiveSequenceDefinition sequence =
            Track(ScriptableObject.CreateInstance<ObjectiveSequenceDefinition>());
        TestReflection.SetField(sequence, "objectives", objectives);
        TestReflection.SetField(sequence, "completionResult", GameResultType.Victory);
        TestReflection.SetField(sequence, "failureResult", GameResultType.Defeat);
        return sequence;
    }

    // The array was kept by hand, and a binding left out of it failed the map
    // at runtime with "no scene binding for required objective" - for a binding
    // sitting right there in the scene.
    [Test]
    public void BindingRegistry_CollectsItsOwnBindingsWhenNothingIsListed()
    {
        ObjectiveDefinition door = CreateObjective("door", requiresSceneBinding: true);
        ObjectiveSceneBindingRegistry registry = CreateRegistry();
        ObjectiveSceneBinding binding = CreateBinding(door);
        binding.transform.SetParent(registry.transform, false);

        Assert.That(registry.TryGetBinding(door, out ObjectiveSceneBinding found), Is.True);
        Assert.That(found, Is.SameAs(binding));

        ObjectiveSequenceDefinition sequence = CreateSequence(door);
        Assert.That(registry.IsValidForSequence(sequence, out string error), Is.True, error);
    }

    // A filled-in list is a deliberate choice of what to bind, so it wins and
    // whatever else is under the object stays out.
    [Test]
    public void BindingRegistry_ListedBindingsOverrideWhatIsUnderIt()
    {
        ObjectiveDefinition listed = CreateObjective("listed", requiresSceneBinding: true);
        ObjectiveDefinition unlisted = CreateObjective("unlisted", requiresSceneBinding: true);

        ObjectiveSceneBinding listedBinding = CreateBinding(listed);
        ObjectiveSceneBindingRegistry registry = CreateRegistry(listedBinding);

        ObjectiveSceneBinding unlistedBinding = CreateBinding(unlisted);
        unlistedBinding.transform.SetParent(registry.transform, false);

        Assert.That(registry.TryGetBinding(listed, out _), Is.True);
        Assert.That(registry.TryGetBinding(unlisted, out _), Is.False);
    }

    private ObjectiveSceneBinding CreateBinding(ObjectiveDefinition objective)
    {
        GameObject host = Track(new GameObject($"Binding {objective.name}"));
        ObjectiveSceneBinding binding = host.AddComponent<ObjectiveSceneBinding>();
        TestReflection.SetField(binding, "objective", objective);
        return binding;
    }

    private ObjectiveSceneBindingRegistry CreateRegistry(params ObjectiveSceneBinding[] bindings)
    {
        GameObject host = Track(new GameObject("Objective binding registry"));
        ObjectiveSceneBindingRegistry registry =
            host.AddComponent<ObjectiveSceneBindingRegistry>();
        registry.ConfigureEditor(bindings);
        return registry;
    }

    private NetworkObjectiveFlow CreateObjectiveFlow()
    {
        GameObject host = Track(new GameObject("Objective flow"));
        host.SetActive(false);
        host.AddComponent<NetworkObject>();
        return host.AddComponent<NetworkObjectiveFlow>();
    }

    // The map declares where enemies go, the same way it declares where players
    // go. Leaving the list empty has to mean "the ones under me" or a point
    // added to the map later would be spawned into by nothing.
    [Test]
    public void MapRoot_CollectsTheEnemySpawnPointsUnderItWhenNoneAreListed()
    {
        GameMapRoot mapRoot = CreateMapRoot(spawnPointCount: 0);

        GameObject first = Track(new GameObject("Enemy spawn 1"));
        first.transform.SetParent(mapRoot.transform, false);
        EnemySpawnPoint firstPoint = first.AddComponent<EnemySpawnPoint>();

        GameObject second = Track(new GameObject("Enemy spawn 2"));
        second.transform.SetParent(mapRoot.transform, false);
        second.transform.position = new Vector3(4f, 0f, 0f);
        EnemySpawnPoint secondPoint = second.AddComponent<EnemySpawnPoint>();

        Assert.That(mapRoot.EnemySpawnPoints, Is.EquivalentTo(new[] { firstPoint, secondPoint }));
        Assert.That(secondPoint.Position, Is.EqualTo(new Vector3(4f, 0f, 0f)));

        // A map with nowhere for enemies is a map without them, not an error.
        GameMapRoot bare = CreateMapRoot(spawnPointCount: 0);
        Assert.That(bare.EnemySpawnPoints, Is.Empty);
    }

    private GameMapRoot CreateMapRoot(int spawnPointCount)
    {
        GameObject host = Track(new GameObject("Map root"));
        GameMapRoot mapRoot = host.AddComponent<GameMapRoot>();
        Transform[] spawnPoints = new Transform[spawnPointCount];

        for (int i = 0; i < spawnPointCount; i++)
        {
            GameObject spawnPoint = new($"Spawn {i}");
            spawnPoint.transform.SetParent(host.transform, false);
            spawnPoint.transform.position = new Vector3(i * 10f, 0f, 0f);
            spawnPoints[i] = spawnPoint.transform;
        }

        mapRoot.ConfigureEditor(spawnPoints, null);
        return mapRoot;
    }

    private T Track<T>(T value)
        where T : Object
    {
        cleanup.Add(value);
        return value;
    }
}
