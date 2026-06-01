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

        if (IsServer)
        {
            phase.Value = initialPhase;
            result.Value = GameResultData.None;
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

        if (phase.Value == GamePhase.Playing || IsGameFinished)
        {
            return false;
        }

        gameFinishedRaised = false;
        result.Value = GameResultData.None;
        phase.Value = GamePhase.Playing;
        return true;
    }

    public bool SetPhaseServerOnly(GamePhase nextPhase)
    {
        if (!IsServer)
        {
            Debug.LogError($"{nameof(NetworkGameFlow)} can change phase only on server.", this);
            return false;
        }

        if (IsGameFinished && nextPhase != GamePhase.Finished)
        {
            return false;
        }

        phase.Value = nextPhase;
        return true;
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

        if (resultType == GameResultType.None)
        {
            Debug.LogError($"{nameof(NetworkGameFlow)} received invalid result type.", this);
            return false;
        }

        result.Value = GameResultData.Create(resultType, reason, objectiveId, instigatorClientId);
        phase.Value = GamePhase.Ending;

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
            phase.Value = GamePhase.Finished;
        }

        finishCoroutine = null;
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