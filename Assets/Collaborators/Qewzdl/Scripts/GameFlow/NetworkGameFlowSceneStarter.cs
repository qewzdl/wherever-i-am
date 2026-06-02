using System.Collections;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkGameFlowSceneStarter : NetworkBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private NetworkGameFlow gameFlow;

    [Header("Start")]
    [SerializeField] private bool startOnServerSpawn = true;
    [SerializeField] [Min(0)] private int framesToWaitBeforeStart = 1;

    private Coroutine startRoutine;

    public override void OnNetworkSpawn()
    {
        if (!ValidateSetup())
        {
            enabled = false;
            return;
        }

        if (!IsServer)
        {
            return;
        }

        if (!startOnServerSpawn)
        {
            return;
        }

        StopStartRoutine();
        startRoutine = StartCoroutine(StartMatchAfterNetworkSpawn());
    }

    public override void OnNetworkDespawn()
    {
        StopStartRoutine();
    }

    private void OnDisable()
    {
        StopStartRoutine();
    }

    private void OnValidate()
    {
        framesToWaitBeforeStart = Mathf.Max(0, framesToWaitBeforeStart);
    }

    private IEnumerator StartMatchAfterNetworkSpawn()
    {
        for (int i = 0; i < framesToWaitBeforeStart; i++)
        {
            yield return null;
        }

        startRoutine = null;

        if (!IsServer || !IsSpawned)
        {
            yield break;
        }

        if (!gameFlow.IsSpawned)
        {
            Debug.LogError($"{nameof(NetworkGameFlowSceneStarter)} cannot start match because assigned {nameof(NetworkGameFlow)} is not spawned.", this);
            yield break;
        }

        if (gameFlow.CurrentPhase != GamePhase.Waiting)
        {
            yield break;
        }

        gameFlow.StartMatchServerOnly();
    }

    private bool ValidateSetup()
    {
        if (gameFlow == null)
        {
            Debug.LogError($"{nameof(NetworkGameFlowSceneStarter)} requires assigned {nameof(NetworkGameFlow)}.", this);
            return false;
        }

        return true;
    }

    private void StopStartRoutine()
    {
        if (startRoutine == null)
        {
            return;
        }

        StopCoroutine(startRoutine);
        startRoutine = null;
    }
}