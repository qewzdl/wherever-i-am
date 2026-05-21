using System;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyAttackController : MonoBehaviour, IEnemyValidatedComponent
{
    [SerializeField] private EnemyAttackEffect attackEffect;

    [Tooltip("If enabled, failed attack effects still consume cooldown. Keep disabled for most gameplay cases.")]
    [SerializeField] private bool consumeCooldownOnFailedEffect;

    private EnemyAttackPipeline pipeline;
    private bool invalidStaticConfigurationLogged;

    public event Action<EnemyAttackPhaseEvent> PhaseChanged;
    public event Action<EnemyAttackResult> AttackResolved;

    public EnemyAttackPhase Phase =>
        pipeline != null ? pipeline.Phase : EnemyAttackPhase.Idle;

    public bool IsBusy =>
        pipeline != null && pipeline.IsBusy;

    public bool IsConfigured =>
        ValidateStaticDependencies(false) &&
        ValidateRuntimeDependencies(false);

    public bool HasRequiredDependencies => IsConfigured;

    public bool CanBeInterrupted =>
        pipeline != null && pipeline.CanBeInterrupted;

    private void Awake()
    {
        if (!ValidateStaticDependencies())
        {
            DisableUntilConfigured();
            return;
        }

        BuildPipeline();
    }

    private void OnDestroy()
    {
        DisposePipeline();
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
        if (!ValidateRuntimeDependencies(this, true))
        {
            pipeline?.Stop(attackerPosition);
            DisposePipeline();
            DisableUntilConfigured();
            return;
        }

        if (!EnsurePipeline(this))
        {
            return;
        }

        pipeline.Tick(deltaTime, attackerPosition);
    }

    public EnemyAttackResult TryStartAttack(
        EnemyTarget target,
        EnemyConfig config,
        Vector3 attackerPosition,
        Component logContext
    )
    {
        Component resolvedLogContext = logContext != null ? logContext : this;

        if (!EnsurePipeline(resolvedLogContext))
        {
            return CreateResult(
                EnemyAttackResultType.MissingEffect,
                EnemyTargetIdentity.FromTarget(target),
                attackerPosition,
                target != null ? target.transform.position : default
            );
        }

        return pipeline.TryStartAttack(
            target,
            config,
            attackerPosition,
            resolvedLogContext
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
        pipeline?.Interrupt(reason, attackerPosition);
    }

    public void ResetCooldown()
    {
        if (!EnsurePipeline(this))
        {
            return;
        }

        pipeline.ResetCooldown();
    }

    private bool EnsurePipeline(Component logContext)
    {
        if (pipeline != null)
        {
            return true;
        }

        if (!ValidateRuntimeDependencies(logContext != null ? logContext : this, true))
        {
            DisableUntilConfigured();
            return false;
        }

        BuildPipeline();
        return pipeline != null;
    }

    private void BuildPipeline()
    {
        DisposePipeline();

        EnemyAttackCooldown cooldown = new();
        EnemyAttackContextFactory contextFactory = new();

        pipeline = new EnemyAttackPipeline(
            attackEffect,
            cooldown,
            contextFactory,
            consumeCooldownOnFailedEffect,
            this
        );

        pipeline.PhaseChanged += HandlePhaseChanged;
        pipeline.AttackResolved += HandleAttackResolved;
    }

    private void DisposePipeline()
    {
        if (pipeline == null)
        {
            return;
        }

        pipeline.PhaseChanged -= HandlePhaseChanged;
        pipeline.AttackResolved -= HandleAttackResolved;
        pipeline = null;
    }

    private void HandlePhaseChanged(EnemyAttackPhaseEvent phaseEvent)
    {
        PhaseChanged?.Invoke(phaseEvent);
    }

    private void HandleAttackResolved(EnemyAttackResult result)
    {
        AttackResolved?.Invoke(result);
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