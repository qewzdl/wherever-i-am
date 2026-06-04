using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class EnemyStateSceneLabel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkEnemyController enemyController;
    [SerializeField] private EnemyNetworkState networkState;

    [Header("Drawing")]
    [SerializeField] private bool drawOnlyWhenSelected;
#if UNITY_EDITOR
    [SerializeField] private bool drawTargetInfo = true;
#endif
    [SerializeField] private Vector3 worldOffset = new(0f, 2.4f, 0f);

#if UNITY_EDITOR
    [Header("Editor Style")]
    [SerializeField] private int fontSize = 13;
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private Color idleColor = Color.gray;
    [SerializeField] private Color patrolColor = Color.green;
    [SerializeField] private Color investigateColor = new(1f, 0.7f, 0.1f);
    [SerializeField] private Color chaseColor = Color.red;
    [SerializeField] private Color attackColor = new(1f, 0f, 0f);

    private GUIStyle labelStyle;
#endif

    private void Awake()
    {
        if (RuntimeDebugBuildGuard.DestroyIfDisabled(this))
        {
            return;
        }

        CacheComponents();
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (drawOnlyWhenSelected)
        {
            return;
        }

        DrawStateLabel();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawOnlyWhenSelected)
        {
            return;
        }

        DrawStateLabel();
    }

    private void DrawStateLabel()
    {
        CacheComponents();

        if (!TryGetState(out EnemyState state))
        {
            return;
        }

        Vector3 labelPosition = transform.position + worldOffset;

        DrawStateMarker(labelPosition, state);
        DrawLabel(labelPosition, state);
    }

    private bool TryGetState(out EnemyState state)
    {
        if (networkState != null)
        {
            state = networkState.CurrentState;
            return true;
        }

        if (enemyController != null)
        {
            state = enemyController.CurrentState;
            return true;
        }

        state = EnemyState.Idle;
        return false;
    }

    private void DrawStateMarker(Vector3 position, EnemyState state)
    {
        Gizmos.color = GetStateColor(state);
        Gizmos.DrawSphere(position, 0.12f);
        Gizmos.DrawLine(transform.position, position);
    }

    private void DrawLabel(Vector3 position, EnemyState state)
    {
        EnsureLabelStyle();

        Color previousColor = labelStyle.normal.textColor;
        labelStyle.normal.textColor = GetStateColor(state);

        string label = BuildLabelText(state);

        Handles.Label(position + Vector3.up * 0.18f, label, labelStyle);

        labelStyle.normal.textColor = previousColor;
    }

    private string BuildLabelText(EnemyState state)
    {
        if (!drawTargetInfo)
        {
            return $"Enemy: {state}";
        }

        bool hasTarget = false;
        ulong targetClientId = EnemyTargetMemory.NoTargetClientId;

        if (enemyController != null)
        {
            hasTarget = enemyController.HasTarget;
            targetClientId = enemyController.CurrentTargetClientId;
        }
        else if (networkState != null)
        {
            hasTarget = networkState.HasTarget;
            targetClientId = networkState.CurrentTargetClientId;
        }

        if (!hasTarget)
        {
            return $"Enemy: {state}\nTarget: None";
        }

        return $"Enemy: {state}\nTarget ClientId: {targetClientId}";
    }

    private Color GetStateColor(EnemyState state)
    {
        return state switch
        {
            EnemyState.Idle => idleColor,
            EnemyState.Patrol => patrolColor,
            EnemyState.Investigate => investigateColor,
            EnemyState.Chase => chaseColor,
            EnemyState.Attack => attackColor,
            _ => textColor
        };
    }

    private void EnsureLabelStyle()
    {
        if (labelStyle != null)
        {
            return;
        }

        labelStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = Mathf.Max(8, fontSize),
            alignment = TextAnchor.MiddleCenter
        };

        labelStyle.normal.textColor = textColor;
    }

    private void Reset()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        CacheComponents();

        fontSize = Mathf.Max(8, fontSize);
        labelStyle = null;
    }
#endif

    private void CacheComponents()
    {
        if (enemyController == null)
        {
            enemyController = GetComponent<NetworkEnemyController>();
        }

        if (networkState == null)
        {
            networkState = GetComponent<EnemyNetworkState>();
        }
    }
}
