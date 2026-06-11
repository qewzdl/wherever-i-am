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
                filter))
        {
            return false;
        }

        agent.enabled = false;
        transform.position = hit.position;
        agent.enabled = true;

        return agent.isOnNavMesh;
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
