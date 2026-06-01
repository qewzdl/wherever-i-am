using System;
using System.Collections;
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

    private Coroutine finishCoroutine;
    private bool matchFinishedRaised;

    public event Action<GamePhase, GamePhase> PhaseChanged;
    public event Action<GameResultData, GameResultData> ResultChanged;
    public event Action<GameResultData> MatchFinished;

    public GamePhase CurrentPhase => phase.Value;
    public GameResultData CurrentResult => result.Value;
    public bool IsMatchRunning => phase.Value == GamePhase.Playing;
    public bool IsMatchFinished => IsTerminalPhase(phase.Value);

    public override void OnNetworkSpawn()
    {
        matchFinishedRaised = phase.Value == GamePhase.Finished;

        phase.OnValueChanged += HandlePhaseChanged;
        result.OnValueChanged += HandleResultChanged;

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

        result.Value = GameResultData.None;
        phase.Value = initialPhase;
        matchFinishedRaised = false;
    }

    public override void OnNetworkDespawn()
    {
        phase.OnValueChanged -= HandlePhaseChanged;
        result.OnValueChanged -= HandleResultChanged;

        StopFinishRoutine();

        matchFinishedRaised = false;
    }

    public bool StartMatchServerOnly()
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

        matchFinishedRaised = false;
        result.Value = GameResultData.None;

        if (phase.Value == GamePhase.Waiting)
        {
            if (!TrySetPhaseServerOnly(GamePhase.Starting, true))
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

        return TrySetPhaseServerOnly(GamePhase.Playing, true);
    }

    public bool CompleteMatchServerOnly(GameResultData matchResult)
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

        if (!TrySetPhaseServerOnly(GamePhase.MatchResolved, true))
        {
            return false;
        }

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

        if (!TrySetPhaseServerOnly(GamePhase.Ending, true))
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
            TrySetPhaseServerOnly(GamePhase.Finished, true);
        }

        finishCoroutine = null;
    }

    private bool TrySetPhaseServerOnly(GamePhase nextPhase, bool logInvalidTransition)
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

    private void HandlePhaseChanged(GamePhase previousValue, GamePhase newValue)
    {
        PhaseChanged?.Invoke(previousValue, newValue);

        if (newValue == GamePhase.Finished)
        {
            RaiseMatchFinishedOnce();
        }
    }

    private void HandleResultChanged(GameResultData previousValue, GameResultData newValue)
    {
        ResultChanged?.Invoke(previousValue, newValue);
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
}