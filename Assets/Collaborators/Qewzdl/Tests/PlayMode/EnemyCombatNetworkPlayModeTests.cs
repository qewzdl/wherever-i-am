using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

internal sealed class EnemyAttackEffectPlayModeProbe : IEnemyAttackEffect
{
    internal int ApplyCount { get; private set; }
    internal bool Result { get; set; } = true;

    public bool TryApply(EnemyAttackContext context)
    {
        ApplyCount++;
        return Result;
    }
}

[Category("Multiplayer")]
public sealed class EnemyCombatNetworkPlayModeTests
{
    private const float StopTimeout = 5f;

    private readonly List<Object> cleanup = new();
    private NetworkManager manager;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (manager != null && manager.IsListening)
            manager.Shutdown(discardMessageQueue: true);

        float timeout = Time.realtimeSinceStartup + StopTimeout;

        while (manager != null &&
               manager.IsListening &&
               Time.realtimeSinceStartup < timeout)
        {
            yield return null;
        }

        for (int i = cleanup.Count - 1; i >= 0; i--)
        {
            if (cleanup[i] != null)
                Object.DestroyImmediate(cleanup[i]);
        }

        cleanup.Clear();
        manager = null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator SpawnedTarget_AttackCommitsOnceAndEnforcesCooldownAndRange()
    {
        manager = CreateHost();
        Assert.That(manager.StartHost(), Is.True);

        float timeout = Time.realtimeSinceStartup + StopTimeout;

        while (!manager.IsHost && Time.realtimeSinceStartup < timeout)
            yield return null;

        Assert.That(manager.IsHost, Is.True);

        EnemyTarget target = CreateSpawnedTarget(new Vector3(1f, 0f, 0f));
        EnemyConfig config = CreateAttackConfig();
        EnemyAttackEffectPlayModeProbe effect = new();
        EnemyAttackCooldown cooldown = new();
        EnemyAttackPipeline pipeline = new(
            effect,
            cooldown,
            new EnemyAttackContextFactory(),
            new EnemyLineOfHitValidator(),
            consumeCooldownOnFailedEffect: false,
            target);

        List<EnemyAttackResultType> results = new();
        pipeline.AttackResolved += result => results.Add(result.Type);

        EnemyAttackResult started =
            pipeline.TryStartAttack(target, config, Vector3.zero, target);

        Assert.That(started.Type, Is.EqualTo(EnemyAttackResultType.Started));
        Assert.That(effect.ApplyCount, Is.EqualTo(1));
        Assert.That(results, Is.EqualTo(new[] { EnemyAttackResultType.Hit }));
        Assert.That(pipeline.Phase, Is.EqualTo(EnemyAttackPhase.Idle));
        Assert.That(cooldown.IsActive, Is.True);

        EnemyAttackResult duringCooldown =
            pipeline.TryStartAttack(target, config, Vector3.zero, target);
        Assert.That(
            duringCooldown.Type,
            Is.EqualTo(EnemyAttackResultType.CooldownActive));
        Assert.That(effect.ApplyCount, Is.EqualTo(1));

        pipeline.Tick(1.1f, Vector3.zero);
        target.transform.position = new Vector3(20f, 0f, 0f);
        EnemyAttackResult outOfRange =
            pipeline.TryStartAttack(target, config, Vector3.zero, target);

        Assert.That(outOfRange.Type, Is.EqualTo(EnemyAttackResultType.OutOfRange));
        Assert.That(effect.ApplyCount, Is.EqualTo(1));
    }

    private NetworkManager CreateHost()
    {
        GameObject root = Track(new GameObject("Enemy combat host"));
        UnityTransport transport = root.AddComponent<UnityTransport>();
        NetworkManager networkManager = root.AddComponent<NetworkManager>();
        networkManager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            EnableSceneManagement = false,
            ProtocolVersion = 3
        };
        transport.SetConnectionData("127.0.0.1", 0, "127.0.0.1");
        return networkManager;
    }

    private EnemyTarget CreateSpawnedTarget(Vector3 position)
    {
        GameObject targetObject = Track(new GameObject("Spawned attack target"));
        targetObject.SetActive(false);
        targetObject.transform.position = position;
        NetworkObject networkObject = targetObject.AddComponent<NetworkObject>();
        BoxCollider collider = targetObject.AddComponent<BoxCollider>();
#if UNITY_EDITOR
        LogAssert.Expect(
            LogType.Error,
            new Regex("EnemyTarget has invalid visibility configuration:"));
#endif
        EnemyTarget target = targetObject.AddComponent<EnemyTarget>();
        PlayModeTestReflection.SetField(
            target,
            "visibilityColliders",
            new Collider[] { collider });
        targetObject.SetActive(true);

        PlayModeTestReflection.SetField(
            networkObject,
            "NetworkManagerOwner",
            manager);
        networkObject.Spawn();
        Assert.That(networkObject.IsSpawned, Is.True);
        return target;
    }

    private EnemyConfig CreateAttackConfig()
    {
        EnemyConfig config = Track(ScriptableObject.CreateInstance<EnemyConfig>());
        EnemyMovementConfig movement =
            Track(ScriptableObject.CreateInstance<EnemyMovementConfig>());
        EnemyAttackTimingConfig timing =
            Track(ScriptableObject.CreateInstance<EnemyAttackTimingConfig>());
        EnemyAttackHitValidationConfig hit =
            Track(ScriptableObject.CreateInstance<EnemyAttackHitValidationConfig>());

        movement.stoppingDistance = 0f;
        timing.attackCooldown = 1f;
        timing.attackWindupDuration = 0f;
        timing.attackCommitDuration = 0f;
        timing.attackRecoveryDuration = 0f;
        timing.attackInterruptedDuration = 0f;
        hit.attackDistance = 2f;
        hit.attackCommitDistanceTolerance = 0f;
        hit.validateLineOfHit = false;

        PlayModeTestReflection.SetField(config, "movementProfile", movement);
        PlayModeTestReflection.SetField(config, "attackTimingProfile", timing);
        PlayModeTestReflection.SetField(
            config,
            "attackHitValidationProfile",
            hit);
        return config;
    }

    private T Track<T>(T value)
        where T : Object
    {
        cleanup.Add(value);
        return value;
    }
}
