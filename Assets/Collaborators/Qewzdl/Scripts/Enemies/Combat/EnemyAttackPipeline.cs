using System;
using UnityEngine;

public sealed class EnemyAttackPipeline
{
    private readonly IEnemyAttackEffect attackEffect;
    private readonly EnemyAttackCooldown cooldown;
    private readonly EnemyAttackContextFactory contextFactory;
    private readonly EnemyLineOfHitValidator lineOfHitValidator;
    private readonly bool consumeCooldownOnFailedEffect;
    private readonly Component defaultLogContext;

    private float phaseTimer;

    private EnemyAttackPhase phase = EnemyAttackPhase.Idle;
    private EnemyAttackContext pendingContext;
    private EnemyTarget pendingTarget;
    private EnemyTargetIdentity pendingTargetIdentity = EnemyTargetIdentity.None;
    private EnemyConfig activeConfig;
    private Component activeLogContext;

    private bool commitApplied;

    public event Action<EnemyAttackPhaseEvent> PhaseChanged;
    public event Action<EnemyAttackResult> AttackResolved;

    public EnemyAttackPhase Phase => phase;

    public bool IsBusy => phase != EnemyAttackPhase.Idle;

    public bool CanBeInterrupted =>
        phase == EnemyAttackPhase.AttackWindup ||
        (phase == EnemyAttackPhase.AttackCommit && !commitApplied);

    public EnemyAttackPipeline(
        IEnemyAttackEffect attackEffect,
        EnemyAttackCooldown cooldown,
        EnemyAttackContextFactory contextFactory,
        EnemyLineOfHitValidator lineOfHitValidator,
        bool consumeCooldownOnFailedEffect,
        Component defaultLogContext
    )
    {
        this.attackEffect = attackEffect ?? throw new ArgumentNullException(
            nameof(attackEffect),
            $"{nameof(EnemyAttackPipeline)} requires non-null {nameof(attackEffect)}."
        );

        this.cooldown = cooldown ?? throw new ArgumentNullException(
            nameof(cooldown),
            $"{nameof(EnemyAttackPipeline)} requires non-null {nameof(cooldown)}."
        );

        this.contextFactory = contextFactory ?? throw new ArgumentNullException(
            nameof(contextFactory),
            $"{nameof(EnemyAttackPipeline)} requires non-null {nameof(contextFactory)}."
        );

        this.lineOfHitValidator = lineOfHitValidator ?? throw new ArgumentNullException(
            nameof(lineOfHitValidator),
            $"{nameof(EnemyAttackPipeline)} requires non-null {nameof(lineOfHitValidator)}."
        );

        this.defaultLogContext = defaultLogContext ?? throw new ArgumentNullException(
            nameof(defaultLogContext),
            $"{nameof(EnemyAttackPipeline)} requires non-null {nameof(defaultLogContext)}."
        );

        this.consumeCooldownOnFailedEffect = consumeCooldownOnFailedEffect;
    }

    public void Tick(float deltaTime, Vector3 attackerPosition)
    {
        cooldown.Tick(deltaTime);

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
                TickRecovery(deltaTime, attackerPosition);
                break;

            case EnemyAttackPhase.AttackInterrupted:
                TickInterrupted(deltaTime, attackerPosition);
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
        Component resolvedLogContext = logContext != null
            ? logContext
            : defaultLogContext;

        if (IsBusy)
        {
            return CreateResult(
                EnemyAttackResultType.Busy,
                pendingTargetIdentity,
                attackerPosition,
                GetCurrentTargetPosition()
            );
        }

        if (cooldown.IsActive)
        {
            return CreateResult(
                EnemyAttackResultType.CooldownActive,
                EnemyTargetIdentity.None,
                attackerPosition,
                default
            );
        }

        if (!contextFactory.TryCreate(
                target,
                config,
                attackerPosition,
                resolvedLogContext,
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
        activeLogContext = resolvedLogContext;
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

    public bool TryValidateAttackTarget(
        EnemyTarget target,
        EnemyConfig config,
        Vector3 attackerPosition,
        Component logContext,
        out EnemyAttackResultType failureType
    )
    {
        Component resolvedLogContext = logContext != null
            ? logContext
            : defaultLogContext;

        if (!contextFactory.TryCreate(
                target,
                config,
                attackerPosition,
                resolvedLogContext,
                config != null ? config.attackDistance : 0f,
                out EnemyAttackContext context,
                out failureType
            ))
        {
            return false;
        }

        return lineOfHitValidator.TryValidate(context, out failureType);
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

    public void Stop(Vector3 attackerPosition)
    {
        if (phase == EnemyAttackPhase.Idle)
        {
            return;
        }

        FinishPipeline(attackerPosition);
    }

    public void ResetCooldown()
    {
        cooldown.Reset();
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

    private void TickRecovery(float deltaTime, Vector3 attackerPosition)
    {
        phaseTimer -= deltaTime;

        if (phaseTimer > 0f)
        {
            return;
        }

        FinishPipeline(attackerPosition);
    }

    private void TickInterrupted(float deltaTime, Vector3 attackerPosition)
    {
        phaseTimer -= deltaTime;

        if (phaseTimer > 0f)
        {
            return;
        }

        EnterRecovery(attackerPosition);
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

        if (activeConfig == null)
        {
            InterruptInternal(EnemyAttackResultType.InvalidTarget, attackerPosition);
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

        if (!lineOfHitValidator.TryValidate(
                pendingContext,
                out EnemyAttackResultType lineOfHitFailureType
            ))
        {
            InterruptInternal(lineOfHitFailureType, attackerPosition);
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
        if (consumeCooldown && activeConfig != null)
        {
            cooldown.Start(activeConfig.attackCooldown);
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
            FinishPipeline(attackerPosition);
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
            FinishPipeline(attackerPosition);
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

    private void FinishPipeline(Vector3 attackerPosition)
    {
        SetPhase(
            EnemyAttackPhase.Idle,
            pendingTargetIdentity,
            attackerPosition,
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
        failureType = EnemyAttackResultType.None;

        if (activeConfig == null)
        {
            failureType = EnemyAttackResultType.InvalidTarget;
            return false;
        }

        return contextFactory.TryCreate(
            pendingTarget,
            activeConfig,
            attackerPosition,
            activeLogContext,
            maxDistance,
            out pendingContext,
            out failureType
        );
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
}
