using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public sealed class NetworkGameFlow : NetworkBehaviour
{
    [Header("State")]
    [SerializeField] private GamePhase initialPhase = GamePhase.WaitingForPlayers;
    [SerializeField] private float finishDelaySeconds = 1.5f;

    private readonly NetworkVariable<GamePhase> phase = new NetworkVariable<GamePhase>(
        GamePhase.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<GameResultData> result = new NetworkVariable<GameResultData>(
        GameResultData.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Coroutine finishCoroutine;
    private bool gameFinishedRaised;

    public event Action<GamePhase, GamePhase> PhaseChanged;
    public event Action<GameResultData, GameResultData> ResultChanged;
    public event Action<GameResultData> GameFinished;

    public GamePhase CurrentPhase => phase.Value;
    public GameResultData CurrentResult => result.Value;
    public bool IsGameRunning => phase.Value == GamePhase.Playing;
    public bool IsGameFinished => phase.Value == GamePhase.Ending || phase.Value == GamePhase.Finished;

    public override void OnNetworkSpawn()
    {
        gameFinishedRaised = phase.Value == GamePhase.Finished;

        phase.OnValueChanged += HandlePhaseChanged;
        result.OnValueChanged += HandleResultChanged;

        if (!IsServer)
        {
            return;
        }

        if (initialPhase != GamePhase.WaitingForPlayers)
        {
            Debug.LogError(
                $"{nameof(NetworkGameFlow)} requires initial phase {nameof(GamePhase.WaitingForPlayers)}. Current configured value: {initialPhase}.",
                this);

            enabled = false;
            return;
        }

        result.Value = GameResultData.None;

        if (!TrySetPhaseServerOnly(initialPhase, true))
        {
            enabled = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        phase.OnValueChanged -= HandlePhaseChanged;
        result.OnValueChanged -= HandleResultChanged;

        if (finishCoroutine != null)
        {
            StopCoroutine(finishCoroutine);
            finishCoroutine = null;
        }

        gameFinishedRaised = false;
    }

    public bool StartGameServerOnly()
    {
        if (!IsServer)
        {
            Debug.LogError($"{nameof(NetworkGameFlow)} can start game only on server.", this);
            return false;
        }

        if (IsGameFinished)
        {
            return false;
        }

        if (phase.Value == GamePhase.Playing)
        {
            return false;
        }

        gameFinishedRaised = false;
        result.Value = GameResultData.None;

        if (phase.Value == GamePhase.WaitingForPlayers)
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

    public bool SetPhaseServerOnly(GamePhase nextPhase)
    {
        if (!IsServer)
        {
            Debug.LogError($"{nameof(NetworkGameFlow)} can change phase only on server.", this);
            return false;
        }

        return TrySetPhaseServerOnly(nextPhase, true);
    }

    public bool FinishGameServerOnly(
        GameResultType resultType,
        string reason,
        string objectiveId,
        ulong instigatorClientId)
    {
        if (!IsServer)
        {
            Debug.LogError($"{nameof(NetworkGameFlow)} can finish game only on server.", this);
            return false;
        }

        if (IsGameFinished)
        {
            return false;
        }

        if (phase.Value != GamePhase.Playing)
        {
            Debug.LogError(
                $"{nameof(NetworkGameFlow)} can finish game only from {nameof(GamePhase.Playing)}. Current phase: {phase.Value}.",
                this);

            return false;
        }

        if (resultType == GameResultType.None)
        {
            Debug.LogError($"{nameof(NetworkGameFlow)} received invalid result type.", this);
            return false;
        }

        result.Value = GameResultData.Create(resultType, reason, objectiveId, instigatorClientId);

        if (!TrySetPhaseServerOnly(GamePhase.Ending, true))
        {
            return false;
        }

        if (finishCoroutine != null)
        {
            StopCoroutine(finishCoroutine);
        }

        finishCoroutine = StartCoroutine(FinishAfterDelay());
        return true;
    }

    private IEnumerator FinishAfterDelay()
    {
        if (finishDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(finishDelaySeconds);
        }

        if (IsServer)
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
            case GamePhase.None:
                return nextPhase == GamePhase.WaitingForPlayers;

            case GamePhase.WaitingForPlayers:
                return nextPhase == GamePhase.Starting;

            case GamePhase.Starting:
                return nextPhase == GamePhase.Playing;

            case GamePhase.Playing:
                return nextPhase == GamePhase.Ending;

            case GamePhase.Ending:
                return nextPhase == GamePhase.Finished;

            case GamePhase.Finished:
                return false;

            default:
                return false;
        }
    }

    private void HandlePhaseChanged(GamePhase previousValue, GamePhase newValue)
    {
        PhaseChanged?.Invoke(previousValue, newValue);

        if (newValue == GamePhase.Finished)
        {
            RaiseGameFinishedOnce();
        }
    }

    private void HandleResultChanged(GameResultData previousValue, GameResultData newValue)
    {
        ResultChanged?.Invoke(previousValue, newValue);
    }

    private void RaiseGameFinishedOnce()
    {
        if (gameFinishedRaised)
        {
            return;
        }

        gameFinishedRaised = true;
        GameFinished?.Invoke(result.Value);
    }
}