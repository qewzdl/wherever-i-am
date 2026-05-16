using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyPostureController : NetworkBehaviour
{
    private const int StandingClearanceBufferSize = 32;

    private readonly NetworkVariable<EnemyPosture> networkPosture = new(
        EnemyPosture.Standing,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly Collider[] standingClearanceHits = new Collider[StandingClearanceBufferSize];

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
    public float LastPostureChangedTime { get; private set; } = float.NegativeInfinity;
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

        if (CurrentPosture == posture)
        {
            return true;
        }

        if (!TryApplyNavigationPosture(posture))
        {
            return false;
        }

        CurrentPosture = posture;
        LastPostureChangedTime = Time.time;

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

    public bool CanUsePostureAtCurrentPosition(EnemyPosture posture)
    {
        if (config == null)
        {
            return false;
        }

        if (posture == EnemyPosture.Standing &&
            CurrentPosture != EnemyPosture.Standing &&
            !HasStandingPhysicalClearance(transform.position))
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

        if (posture == EnemyPosture.Standing &&
            CurrentPosture != EnemyPosture.Standing &&
            !HasStandingPhysicalClearance(postureHit.position))
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

    private bool HasStandingPhysicalClearance(Vector3 rootPosition)
    {
        if (config == null)
        {
            return false;
        }

        int capsuleDirection = bodyCollider != null ? bodyCollider.direction : 1;

        Vector3 worldCenter = GetScaledWorldPoint(
            rootPosition,
            config.standingBodyColliderCenter
        );

        Vector3 capsuleAxis = transform.TransformDirection(GetCapsuleLocalAxis(capsuleDirection));

        if (capsuleAxis.sqrMagnitude <= 0.001f)
        {
            capsuleAxis = Vector3.up;
        }

        capsuleAxis.Normalize();

        float heightScale = GetCapsuleHeightScale(capsuleDirection);
        float radiusScale = GetCapsuleRadiusScale(capsuleDirection);

        float worldRadius = Mathf.Max(
            0.01f,
            config.standingBodyColliderRadius * radiusScale
        );

        float worldHeight = Mathf.Max(
            worldRadius * 2f,
            config.standingBodyColliderHeight * heightScale
        );

        float skin = Mathf.Max(0f, config.standingClearanceSkin);

        worldRadius = Mathf.Max(0.01f, worldRadius - skin);
        worldHeight = Mathf.Max(worldRadius * 2f, worldHeight - skin * 2f);

        float halfSegmentLength = Mathf.Max(0f, worldHeight * 0.5f - worldRadius);

        Vector3 pointA = worldCenter + capsuleAxis * halfSegmentLength;
        Vector3 pointB = worldCenter - capsuleAxis * halfSegmentLength;

        int hitCount = Physics.OverlapCapsuleNonAlloc(
            pointA,
            pointB,
            worldRadius,
            standingClearanceHits,
            config.standingClearanceMask,
            config.standingClearanceTriggerInteraction
        );

        bool bufferWasFull = hitCount >= standingClearanceHits.Length;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = standingClearanceHits[i];

            if (hit == null)
            {
                continue;
            }

            if (IsSelfCollider(hit))
            {
                continue;
            }

            return false;
        }

        return !bufferWasFull;
    }

    private Vector3 GetScaledWorldPoint(Vector3 rootPosition, Vector3 localPoint)
    {
        Vector3 scaledLocalPoint = Vector3.Scale(localPoint, transform.lossyScale);
        return rootPosition + transform.rotation * scaledLocalPoint;
    }

    private bool IsSelfCollider(Collider hit)
    {
        Transform hitTransform = hit.transform;

        return hitTransform == transform || hitTransform.IsChildOf(transform);
    }

    private Vector3 GetCapsuleLocalAxis(int capsuleDirection)
    {
        return capsuleDirection switch
        {
            0 => Vector3.right,
            1 => Vector3.up,
            2 => Vector3.forward,
            _ => Vector3.up
        };
    }

    private float GetCapsuleHeightScale(int capsuleDirection)
    {
        Vector3 scale = transform.lossyScale;

        return capsuleDirection switch
        {
            0 => Mathf.Abs(scale.x),
            1 => Mathf.Abs(scale.y),
            2 => Mathf.Abs(scale.z),
            _ => Mathf.Abs(scale.y)
        };
    }

    private float GetCapsuleRadiusScale(int capsuleDirection)
    {
        Vector3 scale = transform.lossyScale;

        return capsuleDirection switch
        {
            0 => Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)),
            1 => Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)),
            2 => Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)),
            _ => Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z))
        };
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
        LastPostureChangedTime = Time.time;

        ApplyVisualPosture(nextPosture);
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