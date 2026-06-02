using System;
using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

public sealed class NetworkGameFlow : NetworkBehaviour
{
    [Header("State")]
    [SerializeField] private GamePhase initialPhase = GamePhase.Waiting;
    [FormerlySerializedAs("objectiveCompletedDelaySeconds")]
    [SerializeField] [Min(0f)] private float matchResolvedDelaySeconds = 0.5f;
    [SerializeField] [Min(0f)] private float finishDelaySeconds = 1.5f;

    private readonly NetworkVariable<GamePhase> phase = new NetworkVariable<GamePhase>(
        GamePhase.Waiting,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<GameResultData> result = new NetworkVariable<GameResultData>(
        GameResultData.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<FixedString128Bytes> lastTransitionReason = new NetworkVariable<FixedString128Bytes>(
        "Not started",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Coroutine finishCoroutine;
    private bool matchResolvedRaised;
    private bool matchFinishedRaised;
    private bool serverReady;

    public event Action<GamePhase, GamePhase> PhaseChanged;
    public event Action<GameResultData, GameResultData> ResultChanged;
    public event Action<FixedString128Bytes, FixedString128Bytes> TransitionReasonChanged;
    public event Action ServerReady;
    public event Action<GameResultData> MatchResolved;
    public event Action<GameResultData> MatchFinished;

    public GamePhase CurrentPhase => phase.Value;
    public GameResultData CurrentResult => result.Value;
    public string LastTransitionReason => lastTransitionReason.Value.ToString();
    public bool IsMatchRunning => phase.Value == GamePhase.Playing;
    public bool IsMatchFinished => IsTerminalPhase(phase.Value);
    public bool IsServerReady => IsSpawned && IsServer && serverReady;

    public override void OnNetworkSpawn()
    {
        serverReady = false;
        matchResolvedRaised = IsResolvedPhase(phase.Value) && result.Value.HasResult;
        matchFinishedRaised = phase.Value == GamePhase.Finished;

        phase.OnValueChanged += HandlePhaseChanged;
        result.OnValueChanged += HandleResultChanged;
        lastTransitionReason.OnValueChanged += HandleTransitionReasonChanged;

        if (!IsServer)
        {
            return;
        }

        if (initialPhase != GamePhase.Waiting)
        {
            Debug.LogError(
                $"{nameof(NetworkGameFlow)} requires initial phase {nameof(GamePhase.Waiting)}. Current configured value: {initialPhase}.",
                this);

            enabled = false;
            return;
        }

        StopFinishRoutine();

        result.Value = GameResultData.None;
        phase.Value = initialPhase;
        lastTransitionReason.Value = "GameFlow spawned in Waiting phase";

        matchResolvedRaised = false;
        matchFinishedRaised = false;
        serverReady = true;
        ServerReady?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        phase.OnValueChanged -= HandlePhaseChanged;
        result.OnValueChanged -= HandleResultChanged;
        lastTransitionReason.OnValueChanged -= HandleTransitionReasonChanged;

        StopFinishRoutine();

        serverReady = false;
        matchResolvedRaised = false;
        matchFinishedRaised = false;
    }

    public bool StartMatchServerOnly()
    {
        return StartMatchServerOnly("Match start requested");
    }

    public bool StartMatchServerOnly(string reason)
    {
        if (!IsServer)
        {
            Debug.LogError($"{nameof(NetworkGameFlow)} can start match only on server.", this);
            return false;
        }

        if (IsMatchFinished)
        {
            return false;
        }

        if (phase.Value == GamePhase.Playing)
        {
            return false;
        }

        StopFinishRoutine();

        matchResolvedRaised = false;
        matchFinishedRaised = false;
        result.Value = GameResultData.None;

        if (phase.Value == GamePhase.Waiting)
        {
            if (!TrySetPhaseServerOnly(GamePhase.Starting, GetReason(reason, "Match is starting"), true))
            {
                return false;
            }
        }

        if (phase.Value != GamePhase.Starting)
        {
            Debug.LogError(
                $"{nameof(NetworkGameFlow)} can start playing only from {nameof(GamePhase.Starting)}. Current phase: {phase.Value}.",
                this);

            return false;
        }

        return TrySetPhaseServerOnly(GamePhase.Playing, GetReason(reason, "Match is playing"), true);
    }

    public bool CompleteMatchServerOnly(GameResultData matchResult)
    {
        return CompleteMatchServerOnly(matchResult, matchResult.Reason.ToString());
    }

    public bool CompleteMatchServerOnly(GameResultData matchResult, string reason)
    {
        if (!IsServer)
        {
            Debug.LogError($"{nameof(NetworkGameFlow)} can complete match only on server.", this);
            return false;
        }

        if (IsMatchFinished)
        {
            return false;
        }

        if (phase.Value != GamePhase.Playing)
        {
            Debug.LogError(
                $"{nameof(NetworkGameFlow)} can complete match only from {nameof(GamePhase.Playing)}. Current phase: {phase.Value}.",
                this);

            return false;
        }

        if (!matchResult.HasResult)
        {
            Debug.LogError($"{nameof(NetworkGameFlow)} received invalid match result.", this);
            return false;
        }

        result.Value = matchResult;

        if (!TrySetPhaseServerOnly(GamePhase.MatchResolved, GetReason(reason, "Match resolved"), true))
        {
            return false;
        }

        TryRaiseMatchResolvedOnce();

        StartFinishRoutine();
        return true;
    }

    private void StartFinishRoutine()
    {
        StopFinishRoutine();
        finishCoroutine = StartCoroutine(FinishAfterMatchResolved());
    }

    private void StopFinishRoutine()
    {
        if (finishCoroutine == null)
        {
            return;
        }

        StopCoroutine(finishCoroutine);
        finishCoroutine = null;
    }

    private IEnumerator FinishAfterMatchResolved()
    {
        if (matchResolvedDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(matchResolvedDelaySeconds);
        }

        if (!IsServer || phase.Value != GamePhase.MatchResolved)
        {
            finishCoroutine = null;
            yield break;
        }

        if (!TrySetPhaseServerOnly(GamePhase.Ending, "Match ending delay completed", true))
        {
            finishCoroutine = null;
            yield break;
        }

        if (finishDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(finishDelaySeconds);
        }

        if (IsServer && phase.Value == GamePhase.Ending)
        {
            TrySetPhaseServerOnly(GamePhase.Finished, "Match finished", true);
        }

        finishCoroutine = null;
    }

    private bool TrySetPhaseServerOnly(GamePhase nextPhase, string reason, bool logInvalidTransition)
    {
        GamePhase currentPhase = phase.Value;

        if (currentPhase == nextPhase)
        {
            return false;
        }

        if (!IsValidPhaseTransition(currentPhase, nextPhase))
        {
            if (logInvalidTransition)
            {
                Debug.LogError(
                    $"{nameof(NetworkGameFlow)} rejected invalid phase transition: {currentPhase} -> {nextPhase}.",
                    this);
            }

            return false;
        }

        lastTransitionReason.Value = GetReason(reason, $"{currentPhase} -> {nextPhase}");
        phase.Value = nextPhase;

        return true;
    }

    private bool IsValidPhaseTransition(GamePhase currentPhase, GamePhase nextPhase)
    {
        switch (currentPhase)
        {
            case GamePhase.Waiting:
                return nextPhase == GamePhase.Starting;

            case GamePhase.Starting:
                return nextPhase == GamePhase.Playing;

            case GamePhase.Playing:
                return nextPhase == GamePhase.MatchResolved;

            case GamePhase.MatchResolved:
                return nextPhase == GamePhase.Ending;

            case GamePhase.Ending:
                return nextPhase == GamePhase.Finished;

            case GamePhase.Finished:
                return false;

            default:
                return false;
        }
    }

    private bool IsTerminalPhase(GamePhase value)
    {
        return value == GamePhase.MatchResolved
               || value == GamePhase.Ending
               || value == GamePhase.Finished;
    }

    private bool IsResolvedPhase(GamePhase value)
    {
        return value == GamePhase.MatchResolved
               || value == GamePhase.Ending
               || value == GamePhase.Finished;
    }

    private void HandlePhaseChanged(GamePhase previousValue, GamePhase newValue)
    {
        PhaseChanged?.Invoke(previousValue, newValue);

        if (IsResolvedPhase(newValue))
        {
            TryRaiseMatchResolvedOnce();
        }

        if (newValue == GamePhase.Finished)
        {
            RaiseMatchFinishedOnce();
        }
    }

    private void HandleResultChanged(GameResultData previousValue, GameResultData newValue)
    {
        ResultChanged?.Invoke(previousValue, newValue);

        if (newValue.HasResult)
        {
            TryRaiseMatchResolvedOnce();
        }
    }

    private void HandleTransitionReasonChanged(FixedString128Bytes previousValue, FixedString128Bytes newValue)
    {
        TransitionReasonChanged?.Invoke(previousValue, newValue);
    }

    private void TryRaiseMatchResolvedOnce()
    {
        if (matchResolvedRaised)
        {
            return;
        }

        if (!IsResolvedPhase(phase.Value))
        {
            return;
        }

        if (!result.Value.HasResult)
        {
            return;
        }

        matchResolvedRaised = true;
        MatchResolved?.Invoke(result.Value);
    }

    private void RaiseMatchFinishedOnce()
    {
        if (matchFinishedRaised)
        {
            return;
        }

        matchFinishedRaised = true;
        MatchFinished?.Invoke(result.Value);
    }

    private string GetReason(string reason, string fallback)
    {
        return string.IsNullOrWhiteSpace(reason) ? fallback : reason;
    }
}
