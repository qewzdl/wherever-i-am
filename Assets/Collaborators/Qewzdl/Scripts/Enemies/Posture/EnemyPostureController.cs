using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyPostureController : NetworkBehaviour
{
    private readonly NetworkVariable<EnemyPosture> networkPosture = new(
        EnemyPosture.Standing,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("References")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private CapsuleCollider bodyCollider;
    [SerializeField] private Animator animator;

    [Header("NavMesh Agent Types")]
    [SerializeField] private int standingAgentTypeId = 0;
    [SerializeField] private int crawlingAgentTypeId = 0;

    [Header("Animator")]
    [SerializeField] private string crawlingAnimatorBool = "IsCrawling";

    private EnemyConfig config;

    public EnemyPosture CurrentPosture { get; private set; } = EnemyPosture.Standing;
    public bool IsCrawling => CurrentPosture == EnemyPosture.Crawling;

    private void Awake()
    {
        CacheComponents();
    }

    public override void OnNetworkSpawn()
    {
        networkPosture.OnValueChanged += HandleNetworkPostureChanged;

        CurrentPosture = networkPosture.Value;
        ApplyVisualPosture(CurrentPosture);
    }

    public override void OnNetworkDespawn()
    {
        networkPosture.OnValueChanged -= HandleNetworkPostureChanged;
    }

    public void Configure(EnemyConfig enemyConfig)
    {
        config = enemyConfig;
        CacheComponents();

        if (config == null)
        {
            return;
        }

        ApplyVisualPosture(CurrentPosture);
    }

    public bool TrySetServerPosture(EnemyPosture posture)
    {
        if (IsSpawned && !IsServer)
        {
            return false;
        }

        if (config == null)
        {
            return false;
        }

        if (!TryApplyNavigationPosture(posture))
        {
            return false;
        }

        CurrentPosture = posture;
        ApplyVisualPosture(posture);

        if (IsSpawned && IsServer && networkPosture.Value != posture)
        {
            networkPosture.Value = posture;
        }

        return true;
    }

    public float GetSpeedForPosture(float baseSpeed, EnemyPosture posture)
    {
        if (config == null)
        {
            return baseSpeed;
        }

        if (posture != EnemyPosture.Crawling)
        {
            return baseSpeed;
        }

        return baseSpeed * config.crawlingSpeedMultiplier;
    }

    public int GetAgentTypeIdForPosture(EnemyPosture posture)
    {
        return GetAgentTypeId(posture);
    }

    private bool TryApplyNavigationPosture(EnemyPosture posture)
    {
        CacheComponents();

        if (agent == null)
        {
            return false;
        }

        if (!agent.enabled)
        {
            return true;
        }

        if (!agent.gameObject.activeInHierarchy)
        {
            return false;
        }

        int targetAgentTypeId = GetAgentTypeId(posture);

        float switchSampleRadius = Mathf.Max(0.05f, config.postureSwitchSampleRadius);

        if (!TrySamplePositionForAgentType(
            transform.position,
            targetAgentTypeId,
            switchSampleRadius,
            out NavMeshHit postureHit
        ))
        {
            return false;
        }

        Vector3 flatDelta = postureHit.position - transform.position;
        flatDelta.y = 0f;

        if (flatDelta.sqrMagnitude > switchSampleRadius * switchSampleRadius)
        {
            return false;
        }

        int previousAgentTypeId = agent.agentTypeID;
        float previousHeight = agent.height;
        float previousRadius = agent.radius;
        float previousBaseOffset = agent.baseOffset;
        bool previousStopped = agent.isOnNavMesh ? agent.isStopped : true;
        Vector3 previousPosition = transform.position;

        if (previousAgentTypeId != targetAgentTypeId)
        {
            agent.enabled = false;
            transform.position = postureHit.position;
            ApplyAgentProfile(posture);
            agent.enabled = true;
        }
        else
        {
            ApplyAgentProfile(posture);
        }

        if (TryEnsureAgentOnCurrentNavMesh())
        {
            agent.isStopped = previousStopped;
            return true;
        }

        RestoreAgentProfile(
            previousAgentTypeId,
            previousHeight,
            previousRadius,
            previousBaseOffset,
            previousPosition
        );

        TryEnsureAgentOnCurrentNavMesh();

        return false;
    }

    private void ApplyAgentProfile(EnemyPosture posture)
    {
        if (config == null || agent == null)
        {
            return;
        }

        if (posture == EnemyPosture.Crawling)
        {
            ApplyAgentProfileValues(
                GetAgentTypeId(posture),
                config.crawlingAgentHeight,
                config.crawlingAgentRadius,
                config.crawlingAgentBaseOffset
            );

            return;
        }

        ApplyAgentProfileValues(
            GetAgentTypeId(posture),
            config.standingAgentHeight,
            config.standingAgentRadius,
            config.standingAgentBaseOffset
        );
    }

    private void ApplyAgentProfileValues(
        int agentTypeId,
        float height,
        float radius,
        float baseOffset
    )
    {
        if (agent.agentTypeID != agentTypeId)
        {
            agent.agentTypeID = agentTypeId;
        }

        agent.height = height;
        agent.radius = radius;
        agent.baseOffset = baseOffset;
    }

    private void RestoreAgentProfile(
        int agentTypeId,
        float height,
        float radius,
        float baseOffset,
        Vector3 position
    )
    {
        bool wasEnabled = agent.enabled;

        if (wasEnabled)
        {
            agent.enabled = false;
        }

        transform.position = position;
        ApplyAgentProfileValues(agentTypeId, height, radius, baseOffset);

        if (wasEnabled && agent.gameObject.activeInHierarchy)
        {
            agent.enabled = true;
        }
    }

    private bool TryEnsureAgentOnCurrentNavMesh()
    {
        if (agent == null || !agent.enabled || !agent.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (agent.isOnNavMesh)
        {
            return true;
        }

        float sampleRadius = config != null
            ? Mathf.Max(0.05f, config.postureSwitchSampleRadius)
            : 0.25f;

        if (!TrySamplePositionForAgentType(
            transform.position,
            agent.agentTypeID,
            sampleRadius,
            out NavMeshHit hit
        ))
        {
            return false;
        }

        return agent.Warp(hit.position);
    }

    private bool TrySamplePositionForAgentType(
        Vector3 sourcePosition,
        int agentTypeId,
        float sampleRadius,
        out NavMeshHit hit
    )
    {
        NavMeshQueryFilter filter = new()
        {
            agentTypeID = agentTypeId,
            areaMask = agent != null ? agent.areaMask : NavMesh.AllAreas
        };

        return NavMesh.SamplePosition(
            sourcePosition,
            out hit,
            Mathf.Max(0.05f, sampleRadius),
            filter
        );
    }

    private int GetAgentTypeId(EnemyPosture posture)
    {
        int configuredAgentTypeId = posture == EnemyPosture.Crawling
            ? crawlingAgentTypeId
            : standingAgentTypeId;

        return ResolveAgentTypeId(configuredAgentTypeId);
    }

    private static int ResolveAgentTypeId(int configuredAgentTypeId)
    {
        if (IsKnownAgentTypeId(configuredAgentTypeId))
        {
            return configuredAgentTypeId;
        }

        if (configuredAgentTypeId >= 0 && configuredAgentTypeId < NavMesh.GetSettingsCount())
        {
            return NavMesh.GetSettingsByIndex(configuredAgentTypeId).agentTypeID;
        }

        return configuredAgentTypeId;
    }

    private static bool IsKnownAgentTypeId(int agentTypeId)
    {
        int settingsCount = NavMesh.GetSettingsCount();

        for (int i = 0; i < settingsCount; i++)
        {
            if (NavMesh.GetSettingsByIndex(i).agentTypeID == agentTypeId)
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyVisualPosture(EnemyPosture posture)
    {
        if (config == null)
        {
            return;
        }

        ApplyColliderPosture(posture);
        ApplyAnimatorPosture(posture);
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

    private void HandleNetworkPostureChanged(EnemyPosture previousPosture, EnemyPosture nextPosture)
    {
        CurrentPosture = nextPosture;
        ApplyVisualPosture(nextPosture);
    }

    public bool CanUsePostureAtCurrentPosition(EnemyPosture posture)
    {
        if (config == null)
        {
            return false;
        }

        int agentTypeId = GetAgentTypeId(posture);
        float sampleRadius = Mathf.Max(0.05f, config.postureSwitchSampleRadius);

        if (!TrySamplePositionForAgentType(
            transform.position,
            agentTypeId,
            sampleRadius,
            out NavMeshHit hit
        ))
        {
            return false;
        }

        Vector3 flatDelta = hit.position - transform.position;
        flatDelta.y = 0f;

        return flatDelta.sqrMagnitude <= sampleRadius * sampleRadius;
    }

    private void CacheComponents()
    {
        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
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

#if UNITY_EDITOR
    [ContextMenu("Capture Current Agent Type As Standing")]
    private void CaptureCurrentAgentTypeAsStanding()
    {
        CacheComponents();

        if (agent != null)
        {
            standingAgentTypeId = agent.agentTypeID;
            UnityEditor.EditorUtility.SetDirty(this);
        }
    }

    [ContextMenu("Capture Current Agent Type As Crawling")]
    private void CaptureCurrentAgentTypeAsCrawling()
    {
        CacheComponents();

        if (agent != null)
        {
            crawlingAgentTypeId = agent.agentTypeID;
            UnityEditor.EditorUtility.SetDirty(this);
        }
    }

    private void Reset()
    {
        CacheComponents();

        if (agent != null)
        {
            standingAgentTypeId = agent.agentTypeID;
            crawlingAgentTypeId = agent.agentTypeID;
        }
    }

    private void OnValidate()
    {
        CacheComponents();

        standingAgentTypeId = ResolveAgentTypeId(standingAgentTypeId);
        crawlingAgentTypeId = ResolveAgentTypeId(crawlingAgentTypeId);
    }
#endif
}
