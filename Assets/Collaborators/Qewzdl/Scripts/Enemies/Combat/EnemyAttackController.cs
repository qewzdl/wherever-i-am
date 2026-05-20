using System;
using System.Text;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyAttackController : MonoBehaviour, IEnemyValidatedComponent
{
    [SerializeField] private EnemyAttackEffect attackEffect;

    [Tooltip("If enabled, failed attack effects still consume cooldown. Keep disabled for most gameplay cases.")]
    [SerializeField] private bool consumeCooldownOnFailedEffect;

    private float cooldownTimer;
    private float phaseTimer;

    private EnemyAttackPhase phase = EnemyAttackPhase.Idle;
    private EnemyAttackContext pendingContext;
    private EnemyTarget pendingTarget;
    private EnemyTargetIdentity pendingTargetIdentity = EnemyTargetIdentity.None;
    private EnemyConfig activeConfig;
    private Component activeLogContext;

    private bool commitApplied;
    private bool invalidStaticConfigurationLogged;

    public event Action<EnemyAttackPhaseEvent> PhaseChanged;
    public event Action<EnemyAttackResult> AttackResolved;

    public EnemyAttackPhase Phase => phase;

    public bool IsBusy => phase != EnemyAttackPhase.Idle;

    public bool IsConfigured =>
        ValidateStaticDependencies(false) &&
        ValidateRuntimeDependencies(false);

    public bool HasRequiredDependencies => IsConfigured;

    public bool CanBeInterrupted =>
        phase == EnemyAttackPhase.AttackWindup ||
        (phase == EnemyAttackPhase.AttackCommit && !commitApplied);

    private void Awake()
    {
        if (!ValidateStaticDependencies())
        {
            DisableUntilConfigured();
        }
    }

    public bool ValidateStaticDependencies()
    {
        return ValidateStaticDependencies(this, true);
    }

    public bool ValidateRuntimeDependencies()
    {
        return ValidateRuntimeDependencies(this, true);
    }

    public bool ValidateRequiredDependencies(Component logContext = null)
    {
        return ValidateRuntimeDependencies(logContext != null ? logContext : this, true);
    }

    public void Tick(float deltaTime)
    {
        Tick(deltaTime, transform.position);
    }

    public void Tick(float deltaTime, Vector3 attackerPosition)
    {
        if (!ValidateRuntimeDependencies(activeLogContext != null ? activeLogContext : this, true))
        {
            if (phase != EnemyAttackPhase.Idle)
            {
                FinishPipeline();
            }

            DisableUntilConfigured();
            return;
        }

        TickCooldown(deltaTime);

        if (phase == EnemyAttackPhase.Idle)
        {
            return;
        }

        switch (phase)
        {
            case EnemyAttackPhase.AttackWindup:
                TickWindup(deltaTime, attackerPosition);
                break;

            case EnemyAttackPhase.AttackCommit:
                TickCommit(deltaTime, attackerPosition);
                break;

            case EnemyAttackPhase.AttackRecovery:
                TickRecovery(deltaTime);
                break;

            case EnemyAttackPhase.AttackInterrupted:
                TickInterrupted(deltaTime);
                break;
        }
    }

    public EnemyAttackResult TryStartAttack(
        EnemyTarget target,
        EnemyConfig config,
        Vector3 attackerPosition,
        Component logContext
    )
    {
        if (!ValidateRuntimeDependencies(logContext != null ? logContext : this, true))
        {
            DisableUntilConfigured();

            return CreateResult(
                EnemyAttackResultType.MissingEffect,
                EnemyTargetIdentity.FromTarget(target),
                attackerPosition,
                target != null ? target.transform.position : default
            );
        }

        if (IsBusy)
        {
            return CreateResult(
                EnemyAttackResultType.Busy,
                pendingTargetIdentity,
                attackerPosition,
                GetCurrentTargetPosition()
            );
        }

        if (cooldownTimer > 0f)
        {
            return CreateResult(
                EnemyAttackResultType.CooldownActive,
                EnemyTargetIdentity.None,
                attackerPosition,
                default
            );
        }

        if (!TryCreateContext(
                target,
                config,
                attackerPosition,
                logContext,
                config != null ? config.attackDistance : 0f,
                out EnemyAttackContext context,
                out EnemyAttackResultType failureType
            ))
        {
            return CreateResult(
                failureType,
                EnemyTargetIdentity.FromTarget(target),
                attackerPosition,
                target != null ? target.transform.position : default
            );
        }

        pendingContext = context;
        pendingTarget = target;
        pendingTargetIdentity = context.TargetIdentity;
        activeConfig = config;
        activeLogContext = logContext != null ? logContext : this;
        commitApplied = false;

        SetPhase(
            EnemyAttackPhase.AttackWindup,
            pendingTargetIdentity,
            attackerPosition,
            context.TargetPosition
        );

        phaseTimer = Mathf.Max(0f, config.attackWindupDuration);

        if (phaseTimer <= 0f)
        {
            EnterCommit(attackerPosition);
        }

        return CreateResult(
            EnemyAttackResultType.Started,
            pendingTargetIdentity,
            attackerPosition,
            context.TargetPosition
        );
    }

    public bool TryAttack(
        EnemyTarget target,
        EnemyConfig config,
        Vector3 attackerPosition,
        Component logContext
    )
    {
        EnemyAttackResult result = TryStartAttack(
            target,
            config,
            attackerPosition,
            logContext
        );

        return result.WasStarted;
    }

    public void Interrupt(
        EnemyAttackResultType reason = EnemyAttackResultType.Interrupted
    )
    {
        Interrupt(reason, transform.position);
    }

    public void Interrupt(
        EnemyAttackResultType reason,
        Vector3 attackerPosition
    )
    {
        if (!CanBeInterrupted)
        {
            return;
        }

        InterruptInternal(reason, attackerPosition);
    }

    public void ResetCooldown()
    {
        cooldownTimer = 0f;
    }

    private void TickCooldown(float deltaTime)
    {
        if (cooldownTimer <= 0f)
        {
            return;
        }

        cooldownTimer = Mathf.Max(0f, cooldownTimer - deltaTime);
    }

    private void TickWindup(float deltaTime, Vector3 attackerPosition)
    {
        phaseTimer -= deltaTime;

        if (!TryRefreshPendingContext(
                attackerPosition,
                activeConfig != null ? activeConfig.attackDistance : 0f,
                out EnemyAttackResultType failureType
            ))
        {
            InterruptInternal(failureType, attackerPosition);
            return;
        }

        if (phaseTimer > 0f)
        {
            return;
        }

        EnterCommit(attackerPosition);
    }

    private void TickCommit(float deltaTime, Vector3 attackerPosition)
    {
        phaseTimer -= deltaTime;

        if (!commitApplied)
        {
            ApplyCommit(attackerPosition);

            if (phase != EnemyAttackPhase.AttackCommit)
            {
                return;
            }
        }

        if (phaseTimer > 0f)
        {
            return;
        }

        EnterRecovery(attackerPosition);
    }

    private void TickRecovery(float deltaTime)
    {
        phaseTimer -= deltaTime;

        if (phaseTimer > 0f)
        {
            return;
        }

        FinishPipeline();
    }

    private void TickInterrupted(float deltaTime)
    {
        phaseTimer -= deltaTime;

        if (phaseTimer > 0f)
        {
            return;
        }

        EnterRecovery(transform.position);
    }

    private void EnterCommit(Vector3 attackerPosition)
    {
        if (activeConfig == null)
        {
            InterruptInternal(EnemyAttackResultType.InvalidTarget, attackerPosition);
            return;
        }

        SetPhase(
            EnemyAttackPhase.AttackCommit,
            pendingTargetIdentity,
            attackerPosition,
            GetCurrentTargetPosition()
        );

        phaseTimer = Mathf.Max(0f, activeConfig.attackCommitDuration);
        commitApplied = false;

        ApplyCommit(attackerPosition);

        if (phase != EnemyAttackPhase.AttackCommit)
        {
            return;
        }

        if (phaseTimer <= 0f)
        {
            EnterRecovery(attackerPosition);
        }
    }

    private void ApplyCommit(Vector3 attackerPosition)
    {
        commitApplied = true;

        if (!ValidateRuntimeDependencies(activeLogContext != null ? activeLogContext : this, true))
        {
            InterruptInternal(EnemyAttackResultType.MissingEffect, attackerPosition);
            DisableUntilConfigured();
            return;
        }

        if (!TryRefreshPendingContext(
                attackerPosition,
                activeConfig.attackCommitMaxDistance,
                out EnemyAttackResultType failureType
            ))
        {
            InterruptInternal(failureType, attackerPosition);
            return;
        }

        bool attackApplied = attackEffect.TryApply(pendingContext);

        if (!attackApplied)
        {
            ResolveCommit(
                EnemyAttackResultType.EffectRejected,
                attackerPosition,
                consumeCooldownOnFailedEffect
            );

            return;
        }

        ResolveCommit(EnemyAttackResultType.Hit, attackerPosition, true);
    }

    private void ResolveCommit(
        EnemyAttackResultType resultType,
        Vector3 attackerPosition,
        bool consumeCooldown
    )
    {
        if (consumeCooldown)
        {
            StartCooldown(activeConfig);
        }

        EnemyAttackResult result = CreateResult(
            resultType,
            pendingTargetIdentity,
            attackerPosition,
            GetCurrentTargetPosition()
        );

        AttackResolved?.Invoke(result);
    }

    private void EnterRecovery(Vector3 attackerPosition)
    {
        if (activeConfig == null)
        {
            FinishPipeline();
            return;
        }

        SetPhase(
            EnemyAttackPhase.AttackRecovery,
            pendingTargetIdentity,
            attackerPosition,
            GetCurrentTargetPosition()
        );

        phaseTimer = Mathf.Max(0f, activeConfig.attackRecoveryDuration);

        if (phaseTimer <= 0f)
        {
            FinishPipeline();
        }
    }

    private void InterruptInternal(
        EnemyAttackResultType reason,
        Vector3 attackerPosition
    )
    {
        EnemyAttackResult result = CreateResult(
            reason,
            pendingTargetIdentity,
            attackerPosition,
            GetCurrentTargetPosition()
        );

        AttackResolved?.Invoke(result);

        SetPhase(
            EnemyAttackPhase.AttackInterrupted,
            pendingTargetIdentity,
            attackerPosition,
            result.TargetPosition,
            reason
        );

        phaseTimer = activeConfig != null
            ? Mathf.Max(0f, activeConfig.attackInterruptedDuration)
            : 0f;

        if (phaseTimer <= 0f)
        {
            EnterRecovery(attackerPosition);
        }
    }

    private void FinishPipeline()
    {
        SetPhase(
            EnemyAttackPhase.Idle,
            pendingTargetIdentity,
            transform.position,
            GetCurrentTargetPosition()
        );

        phaseTimer = 0f;
        commitApplied = false;

        pendingContext = default;
        pendingTarget = null;
        pendingTargetIdentity = EnemyTargetIdentity.None;
        activeConfig = null;
        activeLogContext = null;
    }

    private bool TryRefreshPendingContext(
        Vector3 attackerPosition,
        float maxDistance,
        out EnemyAttackResultType failureType
    )
    {
        return TryCreateContext(
            pendingTarget,
            activeConfig,
            attackerPosition,
            activeLogContext,
            maxDistance,
            out pendingContext,
            out failureType
        );
    }

    private bool TryCreateContext(
        EnemyTarget target,
        EnemyConfig config,
        Vector3 attackerPosition,
        Component logContext,
        float maxDistance,
        out EnemyAttackContext context,
        out EnemyAttackResultType failureType
    )
    {
        context = default;
        failureType = EnemyAttackResultType.None;

        if (target == null || config == null)
        {
            failureType = EnemyAttackResultType.InvalidTarget;
            return false;
        }

        NetworkObject targetNetworkObject = target.NetworkObject;

        if (targetNetworkObject == null || !targetNetworkObject.IsSpawned)
        {
            failureType = EnemyAttackResultType.InvalidTarget;
            return false;
        }

        EnemyTargetIdentity targetIdentity = EnemyTargetIdentity.FromNetworkObject(targetNetworkObject);

        if (!targetIdentity.HasTarget)
        {
            failureType = EnemyAttackResultType.InvalidTarget;
            return false;
        }

        float distanceToTarget = Vector3.Distance(
            attackerPosition,
            targetNetworkObject.transform.position
        );

        if (distanceToTarget > maxDistance)
        {
            failureType = EnemyAttackResultType.OutOfRange;
            return false;
        }

        context = new EnemyAttackContext(
            target,
            targetIdentity,
            targetNetworkObject,
            config,
            attackerPosition,
            logContext != null ? logContext : this
        );

        return true;
    }

    private EnemyAttackResult CreateResult(
        EnemyAttackResultType type,
        EnemyTargetIdentity targetIdentity,
        Vector3 attackerPosition,
        Vector3 targetPosition
    )
    {
        return EnemyAttackResult.Create(
            type,
            targetIdentity,
            attackerPosition,
            targetPosition
        );
    }

    private void SetPhase(
        EnemyAttackPhase nextPhase,
        EnemyTargetIdentity targetIdentity,
        Vector3 attackerPosition,
        Vector3 targetPosition,
        EnemyAttackResultType reason = EnemyAttackResultType.None
    )
    {
        phase = nextPhase;

        PhaseChanged?.Invoke(
            new EnemyAttackPhaseEvent(
                nextPhase,
                targetIdentity,
                attackerPosition,
                targetPosition,
                reason
            )
        );
    }

    private Vector3 GetCurrentTargetPosition()
    {
        if (pendingContext.IsValid)
        {
            return pendingContext.TargetPosition;
        }

        if (pendingTarget != null)
        {
            return pendingTarget.transform.position;
        }

        return default;
    }

    private void StartCooldown(EnemyConfig config)
    {
        if (config == null)
        {
            return;
        }

        cooldownTimer = config.attackCooldown;
    }

    private bool ValidateStaticDependencies(Component logContext, bool logErrors)
    {
        StringBuilder builder = new();

        if (attackEffect == null)
        {
            EnemyValidationLogger.AppendMissingDependency(
                builder,
                nameof(attackEffect)
            );
        }

        return EnemyValidationLogger.ValidateAndLog(
            logContext != null ? logContext : this,
            nameof(EnemyAttackController),
            builder,
            ref invalidStaticConfigurationLogged,
            logErrors,
            "Enemy attack pipeline is disabled until configured."
        );
    }

    private bool ValidateRuntimeDependencies(Component logContext, bool logErrors)
    {
        return ValidateStaticDependencies(logContext, logErrors);
    }

    private bool ValidateStaticDependencies(bool logErrors)
    {
        return ValidateStaticDependencies(this, logErrors);
    }

    private bool ValidateRuntimeDependencies(bool logErrors)
    {
        return ValidateRuntimeDependencies(this, logErrors);
    }

    private void DisableUntilConfigured()
    {
        enabled = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ValidateStaticDependencies(this, true);
    }
#endif
}