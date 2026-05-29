using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyNetworkState))]
public sealed class EnemyThreatMusicSource : NetworkBehaviour
{
    private static readonly Dictionary<EnemyThreatMusicSource, EnemyThreatMusicState> activeThreats = new();

    [Header("Local Reaction")]
    [SerializeField] private bool requireLocalTargetForCombat = true;
    [SerializeField] private bool playSuspiciousWithoutLocalTarget = true;
    [SerializeField] private bool keepLostTargetOnlyForLocalPlayer = true;

    private EnemyNetworkState enemyNetworkState;
    private EnemyThreatMusicState currentLocalThreatState = EnemyThreatMusicState.Calm;
    private bool hadLocalCombatTarget;

    public static event Action<EnemyThreatMusicSource, EnemyThreatMusicState> ThreatStateChanged;
    public static event Action<EnemyThreatMusicSource> SourceRemoved;

    public EnemyThreatMusicState CurrentLocalThreatState => currentLocalThreatState;

    public static void CopyActiveThreatsTo(Dictionary<EnemyThreatMusicSource, EnemyThreatMusicState> target)
    {
        if (target == null)
        {
            return;
        }

        target.Clear();

        foreach (KeyValuePair<EnemyThreatMusicSource, EnemyThreatMusicState> pair in activeThreats)
        {
            if (pair.Key == null)
            {
                continue;
            }

            target[pair.Key] = pair.Value;
        }
    }

    private void Awake()
    {
        enemyNetworkState = GetComponent<EnemyNetworkState>();

        if (enemyNetworkState == null)
        {
            Debug.LogError(
                $"{nameof(EnemyThreatMusicSource)} requires {nameof(EnemyNetworkState)} on the same GameObject.",
                this
            );

            enabled = false;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsClient || enemyNetworkState == null)
        {
            return;
        }

        enemyNetworkState.ThreatMusicStateChanged += HandleThreatMusicStateChanged;
        enemyNetworkState.TargetChanged += HandleTargetChanged;

        RecalculateAndPublish();
    }

    public override void OnNetworkDespawn()
    {
        if (enemyNetworkState != null)
        {
            enemyNetworkState.ThreatMusicStateChanged -= HandleThreatMusicStateChanged;
            enemyNetworkState.TargetChanged -= HandleTargetChanged;
        }

        RemoveFromRegistry();
    }

    public override void OnDestroy()
    {
        RemoveFromRegistry();
        base.OnDestroy();
    }

    private void HandleThreatMusicStateChanged(
        EnemyThreatMusicState previousState,
        EnemyThreatMusicState nextState
    )
    {
        RecalculateAndPublish();
    }

    private void HandleTargetChanged(
        EnemyTargetIdentity previousTarget,
        EnemyTargetIdentity nextTarget
    )
    {
        if (!TryGetLocalClientId(out ulong localClientId))
        {
            RecalculateAndPublish();
            return;
        }

        bool previousWasLocalTarget =
            previousTarget.HasTarget &&
            previousTarget.OwnerClientId == localClientId;

        bool nextIsLocalTarget =
            nextTarget.HasTarget &&
            nextTarget.OwnerClientId == localClientId;

        if (previousWasLocalTarget || nextIsLocalTarget)
        {
            hadLocalCombatTarget = true;
        }

        RecalculateAndPublish();
    }

    private void RecalculateAndPublish()
    {
        EnemyThreatMusicState nextLocalThreatState = ResolveLocalThreatState();

        if (currentLocalThreatState == nextLocalThreatState)
        {
            PublishCurrentState();
            return;
        }

        currentLocalThreatState = nextLocalThreatState;
        PublishCurrentState();
    }

    private EnemyThreatMusicState ResolveLocalThreatState()
    {
        if (enemyNetworkState == null)
        {
            return EnemyThreatMusicState.Calm;
        }

        EnemyThreatMusicState networkThreatState = enemyNetworkState.CurrentThreatMusicState;

        switch (networkThreatState)
        {
            case EnemyThreatMusicState.Calm:
                hadLocalCombatTarget = false;
                return EnemyThreatMusicState.Calm;

            case EnemyThreatMusicState.Suspicious:
                return ResolveSuspiciousThreat();

            case EnemyThreatMusicState.Combat:
                return ResolveCombatThreat();

            case EnemyThreatMusicState.LostTarget:
                return ResolveLostTargetThreat();

            case EnemyThreatMusicState.Dead:
                hadLocalCombatTarget = false;
                return EnemyThreatMusicState.Dead;

            default:
                Debug.LogError(
                    $"{nameof(EnemyThreatMusicSource)} received unsupported threat music state {networkThreatState}.",
                    this
                );

                return EnemyThreatMusicState.Calm;
        }
    }

    private EnemyThreatMusicState ResolveSuspiciousThreat()
    {
        if (IsTargetingLocalClient())
        {
            return EnemyThreatMusicState.Suspicious;
        }

        return playSuspiciousWithoutLocalTarget
            ? EnemyThreatMusicState.Suspicious
            : EnemyThreatMusicState.Calm;
    }

    private EnemyThreatMusicState ResolveCombatThreat()
    {
        bool isTargetingLocalClient = IsTargetingLocalClient();

        if (isTargetingLocalClient)
        {
            hadLocalCombatTarget = true;
            return EnemyThreatMusicState.Combat;
        }

        return requireLocalTargetForCombat
            ? EnemyThreatMusicState.Calm
            : EnemyThreatMusicState.Combat;
    }

    private EnemyThreatMusicState ResolveLostTargetThreat()
    {
        if (!keepLostTargetOnlyForLocalPlayer)
        {
            return EnemyThreatMusicState.LostTarget;
        }

        return hadLocalCombatTarget
            ? EnemyThreatMusicState.LostTarget
            : EnemyThreatMusicState.Calm;
    }

    private bool IsTargetingLocalClient()
    {
        if (!TryGetLocalClientId(out ulong localClientId))
        {
            return false;
        }

        EnemyTargetIdentity targetIdentity = enemyNetworkState.CurrentTargetIdentity;

        return targetIdentity.HasTarget &&
               targetIdentity.OwnerClientId == localClientId;
    }

    private bool TryGetLocalClientId(out ulong localClientId)
    {
        localClientId = 0UL;

        if (NetworkManager == null || !NetworkManager.IsListening)
        {
            Debug.LogError(
                $"{nameof(EnemyThreatMusicSource)} requires an active {nameof(NetworkManager)} to resolve local threat relevance.",
                this
            );

            return false;
        }

        localClientId = NetworkManager.LocalClientId;
        return true;
    }

    private void PublishCurrentState()
    {
        if (currentLocalThreatState == EnemyThreatMusicState.Calm ||
            currentLocalThreatState == EnemyThreatMusicState.Dead)
        {
            activeThreats.Remove(this);
        }
        else
        {
            activeThreats[this] = currentLocalThreatState;
        }

        ThreatStateChanged?.Invoke(this, currentLocalThreatState);
    }

    private void RemoveFromRegistry()
    {
        if (activeThreats.Remove(this))
        {
            SourceRemoved?.Invoke(this);
            return;
        }

        SourceRemoved?.Invoke(this);
    }
}
