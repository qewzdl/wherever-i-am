using System;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyNavMeshStartupGate : MonoBehaviour
{
    [SerializeField] private RuntimeNavMeshBuilder navMeshBuilder;
    [SerializeField] private bool waitForRuntimeNavMesh = true;
    [SerializeField] [Min(0.05f)] private float placementSampleRadius = 2f;
    [SerializeField] private NavMeshAgent agent;

    private bool subscribedToNavMeshBuilder;

    private event Action Ready;

    private void Awake()
    {
        CacheComponents();

        if (waitForRuntimeNavMesh && agent != null)
            agent.enabled = false;
    }

    public bool TryMakeReadyServer()
    {
        CacheComponents();

        if (!waitForRuntimeNavMesh || navMeshBuilder == null)
            return TryEnableAgentOnNavMesh();

        if (navMeshBuilder.HasBuilt)
            return TryEnableAgentOnNavMesh();

        if (navMeshBuilder.BuildIfAllowed())
            return TryEnableAgentOnNavMesh();

        SubscribeToNavMeshBuilder();
        return false;
    }

    public void AddReadyListener(Action listener)
    {
        if (listener == null)
        {
            return;
        }

        Ready -= listener;
        Ready += listener;

        if (IsReady())
        {
            listener.Invoke();
        }
    }

    public void RemoveReadyListener(Action listener)
    {
        if (listener == null)
        {
            return;
        }

        Ready -= listener;
    }

    private bool IsReady()
    {
        bool navMeshReady =
            !waitForRuntimeNavMesh ||
            navMeshBuilder == null ||
            navMeshBuilder.HasBuilt;

        return navMeshReady &&
               agent != null &&
               agent.enabled &&
               agent.isOnNavMesh;
    }

    private void SubscribeToNavMeshBuilder()
    {
        if (subscribedToNavMeshBuilder || navMeshBuilder == null)
        {
            return;
        }

        subscribedToNavMeshBuilder = true;
        navMeshBuilder.AddBuiltListener(OnRuntimeNavMeshBuilt, notifyImmediatelyIfBuilt: false);
    }

    private void UnsubscribeFromNavMeshBuilder()
    {
        if (!subscribedToNavMeshBuilder || navMeshBuilder == null)
        {
            return;
        }

        navMeshBuilder.RemoveBuiltListener(OnRuntimeNavMeshBuilt);
        subscribedToNavMeshBuilder = false;
    }

    private void OnRuntimeNavMeshBuilt(RuntimeNavMeshBuilder builder)
    {
        UnsubscribeFromNavMeshBuilder();

        if (TryEnableAgentOnNavMesh())
        {
            Ready?.Invoke();
            return;
        }

        Debug.LogError(
            $"{nameof(EnemyNavMeshStartupGate)} could not place '{name}' on the built NavMesh.",
            this);
    }

    private void OnDisable()
    {
        UnsubscribeFromNavMeshBuilder();
        Ready = null;
    }

    private bool TryEnableAgentOnNavMesh()
    {
        CacheComponents();

        if (agent == null || !agent.gameObject.activeInHierarchy)
            return false;

        if (agent.enabled && agent.isOnNavMesh)
            return true;

        NavMeshQueryFilter filter = new()
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };

        if (!NavMesh.SamplePosition(
                transform.position,
                out NavMeshHit hit,
                Mathf.Max(0.05f, placementSampleRadius),
                filter) &&
            !TrySampleBelow(filter, out hit))
        {
            return false;
        }

        agent.enabled = false;
        BodyPlacement.Place(transform, GetComponent<Rigidbody>(), hit.position, transform.rotation);
        agent.enabled = true;

        return agent.isOnNavMesh;
    }

    // A spawn point set higher above the floor than the sample reaches left
    // her hanging where it was, no navmesh near enough to put her on. The
    // floor straight below is where she was meant to stand.
    private bool TrySampleBelow(NavMeshQueryFilter filter, out NavMeshHit hit)
    {
        hit = default;

        return Physics.Raycast(
                   transform.position,
                   Vector3.down,
                   out RaycastHit ground,
                   100f,
                   Physics.DefaultRaycastLayers,
                   QueryTriggerInteraction.Ignore) &&
               NavMesh.SamplePosition(
                   ground.point,
                   out hit,
                   Mathf.Max(0.05f, placementSampleRadius),
                   filter);
    }

    private void CacheComponents()
    {
        if (agent == null)
            agent = GetComponent<NavMeshAgent>();
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        placementSampleRadius = Mathf.Max(0.05f, placementSampleRadius);
        CacheComponents();
    }
#endif
}
