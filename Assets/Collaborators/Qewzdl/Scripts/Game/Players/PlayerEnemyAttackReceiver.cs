using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class PlayerEnemyAttackReceiver : MonoBehaviour, IEnemyAttackReceiver
{
    [SerializeField] private NetworkGameFlow gameFlow;
    [SerializeField] private GameResultType hitResult = GameResultType.Defeat;
    [SerializeField] private string hitReason = "A player was caught by an enemy";
    [SerializeField] private bool logReceivedAttack = true;

    private NetworkObject networkObject;
    private bool hasAcceptedHit;

    private void Awake()
    {
        networkObject = GetComponent<NetworkObject>();
    }

    public bool TryReceiveEnemyAttack(EnemyAttackContext context)
    {
        if (!context.IsValid || hasAcceptedHit)
        {
            return false;
        }

        if (networkObject == null)
        {
            networkObject = GetComponent<NetworkObject>();
        }

        if (networkObject == null ||
            !networkObject.IsSpawned ||
            NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsServer)
        {
            return false;
        }

        hasAcceptedHit = true;

        if (logReceivedAttack)
        {
            Debug.Log(
                $"Player received enemy attack: {context.TargetDebugName}.",
                this
            );
        }

        if (gameFlow == null)
        {
            gameFlow = FindFirstObjectByType<NetworkGameFlow>();
        }

        if (gameFlow != null && gameFlow.IsMatchRunning)
        {
            MatchOutcome outcome = MatchOutcomeFactory.FromAllPlayersDead(
                hitResult,
                hitReason
            );

            if (outcome.HasResult)
            {
                gameFlow.CompleteMatchServerOnly(outcome.ToGameResultData(), hitReason);
            }
        }

        return true;
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(hitReason))
        {
            hitReason = "A player was caught by an enemy";
        }

        if (hitResult == GameResultType.None)
        {
            hitResult = GameResultType.Defeat;
        }
    }
}
