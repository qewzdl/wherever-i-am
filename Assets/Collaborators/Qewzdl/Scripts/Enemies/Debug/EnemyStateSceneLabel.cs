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
    [SerializeField] private EnemyServerRuntime serverRuntime;

    [Header("Drawing")]
    [SerializeField] private bool drawOnlyWhenSelected;
#if UNITY_EDITOR
    [SerializeField] private bool drawTargetInfo = true;

    // A line in this block rather than a label of its own: two labels over
    // one enemy sit on top of each other and neither is readable.
    [SerializeField] private bool drawRoom = true;
    [SerializeField] private bool drawSeenByPlayers = true;
    [SerializeField] private bool drawPlayerDistance = true;
    [SerializeField] private float seenProbeHeight = 1.8f;
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

    [SerializeField] private Color backgroundColor = new(0f, 0f, 0f, 0.65f);
    [SerializeField] private bool drawBackground = true;

    private GUIStyle labelStyle;
    private Texture2D backgroundTexture;
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
        string text = $"Enemy: {state}";

        if (drawTargetInfo)
        {
            text += $"\n{BuildTargetLine()}";
        }

        if (drawRoom)
        {
            text += $"\n{BuildRoomLine()}";
            text += $"\n{BuildSearchRoomLine()}";
        }

        if (drawSeenByPlayers)
        {
            text += $"\n{BuildSeenLine()}";
        }

        if (drawPlayerDistance)
        {
            text += $"\n{BuildDistanceLine()}";
        }

        return text;
    }

    // The bare number is not the useful part - which side of the stalking
    // band it falls on is. Reading "8.3" and remembering where the thresholds
    // sit is work this can do instead.
    private string BuildDistanceLine()
    {
        if (!TryGetNearestPlayerDistance(out float distance))
        {
            return "Player: none";
        }

        EnemyConfig config = enemyController != null
            ? enemyController.Config
            : null;

        if (config == null)
        {
            return $"Player: {distance:0.0} m";
        }

        float chase = config.chaseWithoutStalkingDistance;
        float stalk = config.stalkInsteadOfChasingDistance;

        string side = distance <= chase
            ? "chase"
            : distance >= stalk
                ? "stalk"
                : "band";

        return $"Player: {distance:0.0} m [{side} {chase:0.#}-{stalk:0.#}]";
    }

    private bool TryGetNearestPlayerDistance(out float distance)
    {
        distance = float.PositiveInfinity;

        for (int i = 0; i < PlayerGazeNetwork.All.Count; i++)
        {
            PlayerGazeNetwork gaze = PlayerGazeNetwork.All[i];

            if (gaze == null)
            {
                continue;
            }

            distance = Mathf.Min(
                distance,
                Vector3.Distance(transform.position, gaze.transform.position)
            );
        }

        return !float.IsPositiveInfinity(distance);
    }

    // Whichever way the enemy behaves once it can be watched, the first
    // question about it will be "why did it not react" and the answer will
    // usually be that nobody was actually looking at it.
    private string BuildSeenLine()
    {
        if (PlayerGazeNetwork.All.Count == 0)
        {
            return "Seen: nobody looking";
        }

        return PlayerGazeNetwork.IsBodySeenByAnyone(
            transform.position,
            seenProbeHeight)
            ? "Seen: yes"
            : "Seen: no";
    }

    // The room the enemy stands in is not the one the search is held to - it
    // walks out of that one while searching, and the route stays behind.
    // Without this line a correctly filtered route reads as a broken one,
    // and a route held to nothing at all is invisible.
    private string BuildSearchRoomLine()
    {
        EnemyInvestigationDebugData debugData =
            serverRuntime != null ? serverRuntime.InvestigationDebugData : null;

        if (debugData == null || !debugData.IsActive)
        {
            return "Search: idle";
        }

        return string.IsNullOrEmpty(debugData.BoundRoomId)
            ? "Search: unbounded"
            : $"Search: {debugData.BoundRoomId}";
    }

    private string BuildTargetLine()
    {
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

        return hasTarget
            ? $"Target ClientId: {targetClientId}"
            : "Target: None";
    }

    private string BuildRoomLine()
    {
        return RoomVolume.TryGetRoomAt(transform.position, out RoomVolume room)
            ? $"Room: {room.RoomId}"
            : "Room: none";
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
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(7, 7, 5, 5),
            richText = false
        };

        labelStyle.normal.textColor = textColor;

        if (drawBackground)
        {
            labelStyle.normal.background = EnsureBackgroundTexture();
        }
    }

    // Plain text over a lit scene is unreadable against anything bright, and
    // this block is four lines now. A flat panel behind it costs one pixel of
    // texture, kept out of the scene and out of any build.
    private Texture2D EnsureBackgroundTexture()
    {
        if (backgroundTexture != null)
        {
            return backgroundTexture;
        }

        backgroundTexture = new Texture2D(1, 1)
        {
            hideFlags = HideFlags.HideAndDontSave
        };

        backgroundTexture.SetPixel(0, 0, backgroundColor);
        backgroundTexture.Apply();

        return backgroundTexture;
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

        if (backgroundTexture != null)
        {
            DestroyImmediate(backgroundTexture);
            backgroundTexture = null;
        }
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

        if (serverRuntime == null)
        {
            serverRuntime = GetComponent<EnemyServerRuntime>();
        }
    }
}
