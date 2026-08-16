using System;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyPostureController : MonoBehaviour
{
    private const int StandingClearanceBufferSize = 32;

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
    private EnemyNetworkState networkState;

    private bool initializedServer;
    private bool missingNetworkStateLogged;

    private bool postureTransitionInProgress;
    private EnemyPosture transitionTargetPosture = EnemyPosture.Standing;
    private float postureTransitionStartedTime = float.NegativeInfinity;
    private float postureTransitionCompleteTime = float.NegativeInfinity;
    private float nextStandingRecoveryCheckTime;

    public EnemyPosture CurrentPosture { get; private set; } = EnemyPosture.Standing;
    public float LastPostureChangedTime { get; private set; } = float.NegativeInfinity;
    public bool IsCrawling => CurrentPosture == EnemyPosture.Crawling;

    public bool IsPostureTransitionInProgress => postureTransitionInProgress;
    public EnemyPosture TargetPosture => postureTransitionInProgress
        ? transitionTargetPosture
        : CurrentPosture;

    public float PostureTransitionStartedTime => postureTransitionStartedTime;

    public float PostureTransitionDuration => postureTransitionInProgress
        ? Mathf.Max(0f, postureTransitionCompleteTime - postureTransitionStartedTime)
        : 0f;

    // Server-side notification only. Navigation uses it to invalidate a path
    // built for the previous agent type when an asynchronous transition ends.
    public event Action<EnemyPosture> ServerPostureChanged;

    private void Awake()
    {
        CacheComponents();
    }

    private void Update()
    {
        if (!initializedServer || !postureTransitionInProgress)
        {
            return;
        }

        if (Time.time < postureTransitionCompleteTime)
        {
            return;
        }

        CompletePostureTransitionServer();
    }

    public bool TryInitializeServer(
        EnemyConfig enemyConfig,
        EnemyNetworkState enemyNetworkState
    )
    {
        Configure(enemyConfig);

        networkState = enemyNetworkState;
        initializedServer = false;
        ClearPostureTransition();

        if (config == null)
        {
            Debug.LogError($"{nameof(EnemyPostureController)} requires {nameof(EnemyConfig)}.", this);
            return false;
        }

        if (networkState == null)
        {
            Debug.LogError($"{nameof(EnemyPostureController)} requires {nameof(EnemyNetworkState)}.", this);
            return false;
        }

        initializedServer = true;
        missingNetworkStateLogged = false;

        SyncPostureServer(CurrentPosture);

        return true;
    }

    public void ShutdownServer()
    {
        initializedServer = false;
        networkState = null;
        missingNetworkStateLogged = false;
        ClearPostureTransition();
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
        if (!initializedServer)
        {
            return false;
        }

        if (config == null)
        {
            return false;
        }

        if (postureTransitionInProgress)
        {
            if (transitionTargetPosture == posture)
            {
                return true;
            }

            if (CurrentPosture == posture)
            {
                ClearPostureTransition();
                SyncPostureServer(CurrentPosture);
                return true;
            }

            ClearPostureTransition();
        }

        if (CurrentPosture == posture)
        {
            SyncPostureServer(posture);
            return true;
        }

        return TryStartPostureTransitionServer(posture);
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

    public float GetHeightForPosture(EnemyPosture posture)
    {
        if (config == null)
        {
            return agent != null ? agent.height : 1.8f;
        }

        return posture == EnemyPosture.Crawling
            ? config.crawlingAgentHeight
            : config.standingAgentHeight;
    }

    public int GetAgentTypeIdForPosture(EnemyPosture posture)
    {
        return GetAgentTypeId(posture);
    }

    // Which posture a route should be planned with first, asked without
    // changing anything.
    //
    // Two planners need this answer and they must agree. The runtime one used
    // to work it out from a schedule it kept privately, while the tactical one
    // guessed from the current posture - so for the second or so between a
    // crawl beginning and the next recovery check, the visibility of a
    // standing route was checked and a crawling route was walked, or the other
    // way round. The schedule lives here now, and both ask this.
    public EnemyPosture GetPreferredPlanningPosture()
    {
        if (!IsCrawling)
        {
            return EnemyPosture.Standing;
        }

        if (postureTransitionInProgress ||
            Time.time < LastPostureChangedTime + MinimumPostureDuration ||
            Time.time < nextStandingRecoveryCheckTime)
        {
            return EnemyPosture.Crawling;
        }

        return CanUsePostureAtCurrentPosition(EnemyPosture.Standing)
            ? EnemyPosture.Standing
            : EnemyPosture.Crawling;
    }

    // The same question, asked by the one caller that is allowed to book the
    // next check by asking it. Getting up costs path queries, so it is tried
    // on an interval rather than every frame.
    internal bool TryBeginStandingRecoveryCheck()
    {
        if (!IsCrawling)
        {
            return true;
        }

        if (postureTransitionInProgress)
        {
            return false;
        }

        float now = Time.time;
        float nextAllowedByDuration =
            LastPostureChangedTime + MinimumPostureDuration;

        if (now < nextAllowedByDuration)
        {
            nextStandingRecoveryCheckTime = Mathf.Max(
                nextStandingRecoveryCheckTime,
                nextAllowedByDuration
            );
            return false;
        }

        if (now < nextStandingRecoveryCheckTime)
        {
            return false;
        }

        nextStandingRecoveryCheckTime = now + Mathf.Max(
            0.05f,
            config != null ? config.standingRecoveryCheckInterval : 0.05f
        );
        return true;
    }

    internal void ResetStandingRecoverySchedule()
    {
        nextStandingRecoveryCheckTime = 0f;
    }

    // Just onto its belly: nothing is going to get it up again before the
    // minimum time there has passed, so do not spend queries asking.
    internal void DelayStandingRecoveryAfterPostureChange(EnemyPosture posture)
    {
        nextStandingRecoveryCheckTime = posture == EnemyPosture.Crawling
            ? Time.time + MinimumPostureDuration
            : 0f;
    }

    private float MinimumPostureDuration =>
        config != null ? Mathf.Max(0f, config.minPostureDuration) : 0f;

    public bool CanUsePostureAtCurrentPosition(EnemyPosture posture)
    {
        bool requireStandingPhysicalClearance =
            posture == EnemyPosture.Standing &&
            CurrentPosture != EnemyPosture.Standing;

        if (requireStandingPhysicalClearance &&
            (config == null || !HasStandingPhysicalClearance(transform.position)))
        {
            return false;
        }

        return TryGetUsablePosturePosition(
            posture,
            transform.position,
            config != null ? config.postureSwitchSampleRadius : 0.25f,
            requireStandingPhysicalClearance,
            out _
        );
    }

    public bool TryGetUsablePosturePosition(
        EnemyPosture posture,
        Vector3 sourcePosition,
        float sampleRadius,
        out NavMeshHit hit
    )
    {
        return TryGetUsablePosturePosition(
            posture,
            sourcePosition,
            sampleRadius,
            posture == EnemyPosture.Standing,
            out hit
        );
    }

    private bool TryStartPostureTransitionServer(EnemyPosture posture)
    {
        if (!CanUsePostureAtCurrentPosition(posture))
        {
            return false;
        }

        float transitionDuration = Mathf.Max(
            0f,
            config.GetPostureTransitionDuration(CurrentPosture, posture)
        );

        if (transitionDuration <= 0f)
        {
            return CommitPostureServer(posture);
        }

        postureTransitionInProgress = true;
        transitionTargetPosture = posture;
        postureTransitionStartedTime = Time.time;
        postureTransitionCompleteTime = postureTransitionStartedTime + transitionDuration;

        return true;
    }

    private void CompletePostureTransitionServer()
    {
        EnemyPosture targetPosture = transitionTargetPosture;

        ClearPostureTransition();

        if (CommitPostureServer(targetPosture))
        {
            return;
        }

        Debug.LogWarning(
            $"{nameof(EnemyPostureController)} cancelled transition to {targetPosture} because target posture is no longer usable.",
            this
        );

        ApplyVisualPosture(CurrentPosture);
        SyncPostureServer(CurrentPosture);
    }

    private bool CommitPostureServer(EnemyPosture posture)
    {
        if (CurrentPosture == posture)
        {
            SyncPostureServer(posture);
            return true;
        }

        if (!TryApplyNavigationPosture(posture))
        {
            return false;
        }

        CurrentPosture = posture;
        LastPostureChangedTime = Time.time;

        ApplyVisualPosture(posture);
        SyncPostureServer(posture);
        ServerPostureChanged?.Invoke(posture);

        return true;
    }

    private void ClearPostureTransition()
    {
        postureTransitionInProgress = false;
        transitionTargetPosture = CurrentPosture;
        postureTransitionStartedTime = float.NegativeInfinity;
        postureTransitionCompleteTime = float.NegativeInfinity;
    }

    private void SyncPostureServer(EnemyPosture posture)
    {
        if (networkState != null)
        {
            missingNetworkStateLogged = false;
            networkState.SetPostureServer(posture);
            return;
        }

        if (missingNetworkStateLogged)
        {
            return;
        }

        missingNetworkStateLogged = true;

        Debug.LogError(
            $"{nameof(EnemyPostureController)} cannot synchronize posture without {nameof(EnemyNetworkState)}.",
            this
        );
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

    private bool TryGetUsablePosturePosition(
        EnemyPosture posture,
        Vector3 sourcePosition,
        float sampleRadius,
        bool requireStandingPhysicalClearance,
        out NavMeshHit hit
    )
    {
        hit = default;

        if (config == null)
        {
            return false;
        }

        float clampedSampleRadius = Mathf.Max(0.05f, sampleRadius);

        if (!TrySamplePositionForAgentType(
                sourcePosition,
                GetAgentTypeId(posture),
                clampedSampleRadius,
                out hit
            ))
        {
            return false;
        }

        Vector3 flatDelta = hit.position - sourcePosition;
        flatDelta.y = 0f;

        if (flatDelta.sqrMagnitude > clampedSampleRadius * clampedSampleRadius)
        {
            return false;
        }

        return !requireStandingPhysicalClearance ||
               HasStandingPhysicalClearance(hit.position);
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
