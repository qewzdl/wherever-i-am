using System;
using UnityEngine;

public sealed class EnemyStimulusResolver
{
    public EnemyStimulusResolution Resolve(
        EnemyStimulusResolveContext context,
        EnemyStimulusResolverPolicy policy
    )
    {
        if (policy == null)
        {
            throw new ArgumentNullException(
                nameof(policy),
                $"{nameof(EnemyStimulusResolver)} requires explicit {nameof(EnemyStimulusResolverPolicy)}."
            );
        }

        bool hasVision = context.HasVisionStimulus;
        bool hasHearing = context.HasHearingStimulus;

        if (!hasVision && !hasHearing)
        {
            return EnemyStimulusResolution.None;
        }

        if (hasVision && hasHearing)
        {
            return ResolveVisionAndHearing(context, policy);
        }

        if (hasVision)
        {
            return ResolveVisionOnly(context, policy);
        }

        return ResolveHearingOnly(context, policy);
    }

    private EnemyStimulusResolution ResolveVisionAndHearing(
        EnemyStimulusResolveContext context,
        EnemyStimulusResolverPolicy policy
    )
    {
        EnemyPerceptionStimulus visionStimulus = context.VisionStimulus;
        EnemyPerceptionStimulus hearingStimulus = context.HearingStimulus;

        if (policy.VisionAlwaysOverridesHearing)
        {
            return ResolveVisionWithOptionalSecondaryHearing(
                context,
                policy,
                visionStimulus,
                hearingStimulus
            );
        }

        if (ShouldHearingInterruptCurrentTarget(context, policy, hearingStimulus, visionStimulus))
        {
            return EnemyStimulusResolution.Investigate(
                hearingStimulus,
                shouldClearCurrentTarget: true
            );
        }

        if (visionStimulus.IsConfirmedTarget && visionStimulus.HasTarget)
        {
            return ResolveVisionWithOptionalSecondaryHearing(
                context,
                policy,
                visionStimulus,
                hearingStimulus
            );
        }

        if (hearingStimulus.Score > visionStimulus.Score)
        {
            return ResolveHearingOnly(context, policy);
        }

        return ResolveVisionOnly(context, policy);
    }

    private EnemyStimulusResolution ResolveVisionOnly(
        EnemyStimulusResolveContext context,
        EnemyStimulusResolverPolicy policy
    )
    {
        EnemyPerceptionStimulus visionStimulus = context.VisionStimulus;

        if (!visionStimulus.HasStimulus)
        {
            return EnemyStimulusResolution.None;
        }

        if (!visionStimulus.IsConfirmedTarget || !visionStimulus.HasTarget)
        {
            return EnemyStimulusResolution.Investigate(
                visionStimulus,
                shouldClearCurrentTarget: false
            );
        }

        EnemyTargetMemory targetMemory = context.Blackboard?.TargetMemory;

        if (targetMemory == null || !targetMemory.HasTarget)
        {
            return EnemyStimulusResolution.Chase(visionStimulus);
        }

        if (targetMemory.CurrentTarget == visionStimulus.Target)
        {
            return EnemyStimulusResolution.Chase(visionStimulus);
        }

        // Target ownership is decided once, after vision and hearing have been
        // resolved. Keeping a second switch rule here made the vision-only and
        // combined paths disagree and manufactured a sighting for the old
        // target at its last-known position.
        return EnemyStimulusResolution.Chase(visionStimulus);
    }

    private EnemyStimulusResolution ResolveHearingOnly(
        EnemyStimulusResolveContext context,
        EnemyStimulusResolverPolicy policy
    )
    {
        EnemyPerceptionStimulus hearingStimulus = context.HearingStimulus;

        if (!hearingStimulus.HasStimulus)
        {
            return EnemyStimulusResolution.None;
        }

        if (hearingStimulus.IsConfirmedTarget &&
            hearingStimulus.HasTarget &&
            policy.ConfirmedHearingCanStartChase)
        {
            return EnemyStimulusResolution.Chase(hearingStimulus);
        }

        if (IsPursuingConfirmedTarget(context))
        {
            if (ShouldHearingInterruptCurrentTarget(
                    context,
                    policy,
                    hearingStimulus,
                    EnemyPerceptionStimulus.None
                ))
            {
                return EnemyStimulusResolution.Investigate(
                    hearingStimulus,
                    shouldClearCurrentTarget: true
                );
            }

            return EnemyStimulusResolution.RememberSecondarySuspicion(hearingStimulus);
        }

        return EnemyStimulusResolution.Investigate(
            hearingStimulus,
            shouldClearCurrentTarget: false
        );
    }

    private EnemyStimulusResolution ResolveVisionWithOptionalSecondaryHearing(
        EnemyStimulusResolveContext context,
        EnemyStimulusResolverPolicy policy,
        EnemyPerceptionStimulus visionStimulus,
        EnemyPerceptionStimulus hearingStimulus
    )
    {
        if (!visionStimulus.IsConfirmedTarget || !visionStimulus.HasTarget)
        {
            return EnemyStimulusResolution.Investigate(
                visionStimulus,
                shouldClearCurrentTarget: false
            );
        }

        bool shouldRememberHearing =
            hearingStimulus.HasStimulus &&
            policy.RememberHearingWhileChasingVisibleTarget &&
            !IsSameTargetOrPosition(visionStimulus, hearingStimulus);

        if (context.Blackboard != null &&
            context.Blackboard.PerceptionMemory.IsUsingVisualMemory &&
            !policy.RememberHearingWhileUsingVisualMemory)
        {
            shouldRememberHearing = false;
        }

        return EnemyStimulusResolution.Chase(
            visionStimulus,
            hearingStimulus,
            shouldRememberHearing
        );
    }

    private bool ShouldHearingInterruptCurrentTarget(
        EnemyStimulusResolveContext context,
        EnemyStimulusResolverPolicy policy,
        EnemyPerceptionStimulus hearingStimulus,
        EnemyPerceptionStimulus competingVisionStimulus
    )
    {
        if (!policy.AllowHearingToInterruptConfirmedTarget)
        {
            return false;
        }

        if (!hearingStimulus.HasStimulus)
        {
            return false;
        }

        if (!IsPursuingConfirmedTarget(context))
        {
            return false;
        }

        if (IsCurrentVisualTargetTrusted(context, policy))
        {
            return false;
        }

        if (hearingStimulus.Score < policy.MinimumHearingInterruptScore)
        {
            return false;
        }

        if (competingVisionStimulus.HasStimulus)
        {
            float requiredScore = competingVisionStimulus.Score *
                                  Mathf.Max(1f, policy.HearingInterruptScoreMultiplier);

            return hearingStimulus.Score >= requiredScore;
        }

        return true;
    }

    private bool IsPursuingConfirmedTarget(EnemyStimulusResolveContext context)
    {
        EnemyTargetMemory targetMemory = context.Blackboard?.TargetMemory;

        if (targetMemory == null)
        {
            return false;
        }

        return targetMemory.HasTarget &&
               targetMemory.IsCurrentTargetValid &&
               (context.CurrentState == EnemyState.Chase ||
                context.CurrentState == EnemyState.Attack);
    }

    private bool IsCurrentVisualTargetTrusted(
        EnemyStimulusResolveContext context,
        EnemyStimulusResolverPolicy policy
    )
    {
        if (context.Blackboard == null)
        {
            return false;
        }

        if (context.Blackboard.PerceptionMemory.IsUsingVisualMemory)
        {
            return true;
        }

        float lastVisibleTime = context.Blackboard.LastVisibleTime;

        if (lastVisibleTime < 0f)
        {
            return false;
        }

        float elapsed = context.ServerTime - lastVisibleTime;
        return elapsed <= policy.VisualTargetTrustDuration;
    }

    private bool IsSameTargetOrPosition(
        EnemyPerceptionStimulus first,
        EnemyPerceptionStimulus second
    )
    {
        if (first.HasTarget && second.HasTarget)
        {
            return first.Target == second.Target;
        }

        float sqrDistance = (first.Position - second.Position).sqrMagnitude;
        return sqrDistance <= 0.25f;
    }
}
