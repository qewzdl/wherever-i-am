using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class PlayerEnemyAttackReceiver : NetworkBehaviour, IEnemyAttackReceiver
{
    [SerializeField] private GameResultType hitResult = GameResultType.Defeat;
    [SerializeField] private string hitReason = "A player was caught by an enemy";
    [SerializeField] private bool logReceivedAttack = true;

    private NetworkObject networkObject;
    private readonly PlayerEnemyAttackCompletionGate completionGate = new();

    private void Awake()
    {
        networkObject = GetComponent<NetworkObject>();
    }

    public bool TryReceiveEnemyAttack(EnemyAttackContext context)
    {
        if (!context.IsValid || !completionGate.CanAttempt)
        {
            return false;
        }

        if (networkObject == null)
        {
            networkObject = GetComponent<NetworkObject>();
        }

        if (networkObject == null ||
            !networkObject.IsSpawned ||
            NetworkManager == null ||
            !NetworkManager.IsServer)
        {
            return false;
        }

        if (!NetworkObjectServiceContext.TryResolveSessionService(
                NetworkManager,
                out IMatchCompletionService matchCompletionService) ||
            !completionGate.TryComplete(
                matchCompletionService,
                hitResult,
                hitReason))
        {
            return false;
        }

        if (logReceivedAttack)
        {
            Debug.Log(
                $"Player received enemy attack: {context.TargetDebugName}.",
                this
            );
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

internal sealed class PlayerEnemyAttackCompletionGate
{
    private bool hasAcceptedHit;
    private bool isCompletingHit;

    internal bool CanAttempt => !hasAcceptedHit && !isCompletingHit;

    internal bool TryComplete(
        IMatchCompletionService matchCompletionService,
        GameResultType hitResult,
        string hitReason)
    {
        if (!CanAttempt ||
            matchCompletionService == null ||
            !matchCompletionService.IsMatchRunning)
        {
            return false;
        }

        MatchOutcome outcome = MatchOutcomeFactory.FromAllPlayersDead(
            hitResult,
            hitReason
        );
        if (!outcome.HasResult)
        {
            return false;
        }

        isCompletingHit = true;
        try
        {
            if (!matchCompletionService.CompleteMatchServerOnly(
                    outcome.ToGameResultData(),
                    hitReason))
            {
                return false;
            }

            hasAcceptedHit = true;
            return true;
        }
        finally
        {
            isCompletingHit = false;
        }
    }
}
