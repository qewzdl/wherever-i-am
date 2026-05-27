using System.Text;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyNetworkState))]
public class EnemyClientPresentation : MonoBehaviour, IEnemyClientPresentation
{
    [Header("References")]
    [SerializeField] private EnemyNetworkState networkState;
    [SerializeField] private NavMeshAgent localNavigationAgent;
    [SerializeField] private CapsuleCollider bodyCollider;
    [SerializeField] private Animator animator;

    [Header("Animator Parameters")]
    [SerializeField] private string stateAnimatorInt = "";
    [SerializeField] private string crawlingAnimatorBool = "IsCrawling";
    [SerializeField] private string attackPhaseAnimatorInt = "";

    private EnemyConfig config;
    private bool initialized;
    private bool subscribed;

    private void Awake()
    {
        CacheComponents();
    }

    public bool InitializePresentation(
        EnemyConfig enemyConfig,
        EnemyNetworkState enemyNetworkState,
        bool disableLocalNavigationAgent
    )
    {
        ShutdownPresentation();
        CacheComponents();

        config = enemyConfig;
        networkState = enemyNetworkState;

        if (!ValidateDependencies(disableLocalNavigationAgent))
        {
            enabled = false;
            return false;
        }

        if (disableLocalNavigationAgent)
        {
            DisableLocalNavigationAgent();
        }

        Subscribe();

        ApplyState(networkState.CurrentState);
        ApplyPosture(networkState.CurrentPosture);
        ApplyAttackPhase(networkState.CurrentAttackPhase);

        initialized = true;
        enabled = true;

        return true;
    }

    public void ShutdownPresentation()
    {
        Unsubscribe();

        initialized = false;
        config = null;
    }

    private void Subscribe()
    {
        if (subscribed || networkState == null)
        {
            return;
        }

        networkState.StateChanged += HandleStateChanged;
        networkState.PostureChanged += HandlePostureChanged;
        networkState.AttackPhaseChanged += HandleAttackPhaseChanged;

        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || networkState == null)
        {
            subscribed = false;
            return;
        }

        networkState.StateChanged -= HandleStateChanged;
        networkState.PostureChanged -= HandlePostureChanged;
        networkState.AttackPhaseChanged -= HandleAttackPhaseChanged;

        subscribed = false;
    }

    private void DisableLocalNavigationAgent()
    {
        if (localNavigationAgent == null)
        {
            return;
        }

        localNavigationAgent.enabled = false;
    }

    private void HandleStateChanged(EnemyState previousState, EnemyState nextState)
    {
        ApplyState(nextState);
    }

    private void HandlePostureChanged(
        EnemyPosture previousPosture,
        EnemyPosture nextPosture
    )
    {
        ApplyPosture(nextPosture);
    }

    private void HandleAttackPhaseChanged(
        EnemyAttackPhaseSnapshot previousPhase,
        EnemyAttackPhaseSnapshot nextPhase
    )
    {
        ApplyAttackPhase(nextPhase);
    }

    private void ApplyState(EnemyState state)
    {
        if (!initialized && networkState == null)
        {
            return;
        }

        if (animator == null || string.IsNullOrWhiteSpace(stateAnimatorInt))
        {
            return;
        }

        animator.SetInteger(stateAnimatorInt, (int)state);
    }

    private void ApplyPosture(EnemyPosture posture)
    {
        if (config == null)
        {
            return;
        }

        ApplyColliderPosture(posture);
        ApplyAnimatorPosture(posture);
    }

    private void ApplyAttackPhase(EnemyAttackPhaseSnapshot phase)
    {
        if (animator == null || string.IsNullOrWhiteSpace(attackPhaseAnimatorInt))
        {
            return;
        }

        animator.SetInteger(attackPhaseAnimatorInt, (int)phase.Phase);
    }

    private void ApplyColliderPosture(EnemyPosture posture)
    {
        if (bodyCollider == null)
        {
            return;
        }

        if (posture == EnemyPosture.Crawling)
        {
            bodyCollider.height = config.crawlingBodyColliderHeight;
            bodyCollider.radius = config.crawlingBodyColliderRadius;
            bodyCollider.center = config.crawlingBodyColliderCenter;
            return;
        }

        bodyCollider.height = config.standingBodyColliderHeight;
        bodyCollider.radius = config.standingBodyColliderRadius;
        bodyCollider.center = config.standingBodyColliderCenter;
    }

    private void ApplyAnimatorPosture(EnemyPosture posture)
    {
        if (animator == null || string.IsNullOrWhiteSpace(crawlingAnimatorBool))
        {
            return;
        }

        animator.SetBool(crawlingAnimatorBool, posture == EnemyPosture.Crawling);
    }

    private bool ValidateDependencies(bool requireNavigationAgent)
    {
        StringBuilder builder = new();

        if (config == null)
        {
            EnemyValidationLogger.AppendMissingDependency(builder, nameof(config));
        }

        if (networkState == null)
        {
            EnemyValidationLogger.AppendMissingDependency(builder, nameof(networkState));
        }

        if (requireNavigationAgent && localNavigationAgent == null)
        {
            EnemyValidationLogger.AppendMissingDependency(builder, nameof(localNavigationAgent));
        }

        if (config != null && config.crawlingEnabled && bodyCollider == null)
        {
            EnemyValidationLogger.AppendMissingDependency(builder, nameof(bodyCollider));
        }

        if (!ValidateAnimatorParameter(
                builder,
                stateAnimatorInt,
                AnimatorControllerParameterType.Int
            ))
        {
            return false;
        }

        if (!ValidateAnimatorParameter(
                builder,
                crawlingAnimatorBool,
                AnimatorControllerParameterType.Bool
            ))
        {
            return false;
        }

        if (!ValidateAnimatorParameter(
                builder,
                attackPhaseAnimatorInt,
                AnimatorControllerParameterType.Int
            ))
        {
            return false;
        }

        if (builder.Length <= 0)
        {
            return true;
        }

        Debug.LogError(
            $"{nameof(EnemyClientPresentation)} has invalid configuration:\n" +
            builder +
            "Enemy client presentation is disabled until configured.",
            this
        );

        return false;
    }

    private bool ValidateAnimatorParameter(
        StringBuilder builder,
        string parameterName,
        AnimatorControllerParameterType parameterType
    )
    {
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            return true;
        }

        if (animator == null)
        {
            EnemyValidationLogger.AppendMissingDependency(builder, nameof(animator));
            return true;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == parameterName && parameter.type == parameterType)
            {
                return true;
            }
        }

        builder.Append("- ");
        builder.Append(nameof(animator));
        builder.Append(" missing ");
        builder.Append(parameterType);
        builder.Append(" parameter '");
        builder.Append(parameterName);
        builder.AppendLine("'.");

        return true;
    }

    private void CacheComponents()
    {
        if (networkState == null)
        {
            networkState = GetComponent<EnemyNetworkState>();
        }

        if (localNavigationAgent == null)
        {
            localNavigationAgent = GetComponent<NavMeshAgent>();
        }

        if (bodyCollider == null)
        {
            bodyCollider = GetComponentInChildren<CapsuleCollider>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
    }

    private void OnDisable()
    {
        ShutdownPresentation();
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        CacheComponents();
    }
#endif
}
