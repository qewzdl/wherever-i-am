using System;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyNavMeshStartupGate : MonoBehaviour
{
    [SerializeField] private RuntimeNavMeshBuilder navMeshBuilder;
    [SerializeField] private bool waitForRuntimeNavMesh = true;

    private bool subscribedToNavMeshBuilder;

    private event Action Ready;

    public bool TryMakeReadyServer()
    {
        if (!waitForRuntimeNavMesh || navMeshBuilder == null)
        {
            return true;
        }

        if (navMeshBuilder.HasBuilt)
        {
            return true;
        }

        if (navMeshBuilder.BuildIfAllowed())
        {
            return true;
        }

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
        return !waitForRuntimeNavMesh || navMeshBuilder == null || navMeshBuilder.HasBuilt;
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
        Ready?.Invoke();
    }

    private void OnDisable()
    {
        UnsubscribeFromNavMeshBuilder();
        Ready = null;
    }
}