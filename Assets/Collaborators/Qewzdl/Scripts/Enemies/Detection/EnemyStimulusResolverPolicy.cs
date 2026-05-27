using System;
using UnityEngine;

[Serializable]
public sealed class EnemyStimulusResolverPolicy
{
    [Header("Vision")]
    [SerializeField] private bool visionAlwaysOverridesHearing = true;
    [SerializeField] private bool allowSwitchToNewVisibleTarget = true;

    [Header("Hearing Interrupt")]
    [SerializeField] private bool allowHearingToInterruptConfirmedTarget;
    [SerializeField, Min(0f)] private float minimumHearingInterruptScore = 2f;
    [SerializeField, Min(1f)] private float hearingInterruptScoreMultiplier = 2f;

    [Header("Target Trust")]
    [SerializeField, Min(0f)] private float visualTargetTrustDuration = 1.25f;

    [Header("Secondary Stimuli")]
    [SerializeField] private bool rememberHearingWhileChasingVisibleTarget = true;
    [SerializeField] private bool rememberHearingWhileUsingVisualMemory = true;

    [Header("Hearing Behaviour")]
    [SerializeField] private bool confirmedHearingCanStartChase;

    public bool VisionAlwaysOverridesHearing => visionAlwaysOverridesHearing;
    public bool AllowSwitchToNewVisibleTarget => allowSwitchToNewVisibleTarget;

    public bool AllowHearingToInterruptConfirmedTarget => allowHearingToInterruptConfirmedTarget;
    public float MinimumHearingInterruptScore => minimumHearingInterruptScore;
    public float HearingInterruptScoreMultiplier => hearingInterruptScoreMultiplier;

    public float VisualTargetTrustDuration => visualTargetTrustDuration;

    public bool RememberHearingWhileChasingVisibleTarget => rememberHearingWhileChasingVisibleTarget;
    public bool RememberHearingWhileUsingVisualMemory => rememberHearingWhileUsingVisualMemory;

    public bool ConfirmedHearingCanStartChase => confirmedHearingCanStartChase;
}