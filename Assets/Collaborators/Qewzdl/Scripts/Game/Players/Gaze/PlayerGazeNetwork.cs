using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Where a player is looking, known on the server.
//
// Perception in this project only ever ran one way - the enemy sees the
// player - so nothing could answer "is the enemy being watched". Every
// behaviour that reacts to being noticed needs that answer first.
//
// Only the pitch is replicated. The body's yaw already reaches the server
// through the player's NetworkTransform, and the camera turns the body with
// it, so sending a whole direction would send the yaw twice.

// One player who can see a given point, and where they are watching it from.
//
// "Is anybody looking at me" collapses a room full of people into a single
// bool, and every stealth decision made from that bool is made against the
// wrong person: an enemy noticed by player B while working on player A backed
// away from A, which in a shared room is a route towards B.
public readonly struct PlayerWatcher
{
    public ulong ClientId { get; }
    public Vector3 Position { get; }
    public Vector3 EyePosition { get; }
    public Vector3 GazeDirection { get; }

    public PlayerWatcher(
        ulong clientId,
        Vector3 position,
        Vector3 eyePosition,
        Vector3 gazeDirection
    )
    {
        ClientId = clientId;
        Position = position;
        EyePosition = eyePosition;
        GazeDirection = gazeDirection;
    }
}

[DisallowMultipleComponent]
public sealed class PlayerGazeNetwork : NetworkBehaviour
{
    private const float PitchSendThreshold = 1.5f;

    private static readonly List<PlayerGazeNetwork> RegisteredGazes = new();

    [Header("Eye")]
    [Tooltip("Where the camera sits above the player's origin when standing.")]
    [SerializeField] private float eyeHeight = 1.6f;

    [Header("View")]
    [Tooltip("Half angle of the cone a player is taken to see within.")]
    [SerializeField, Range(5f, 90f)] private float halfViewAngle = 60f;

    [SerializeField] private float maxViewDistance = 40f;

    // Geometry only. Everything blocks sight if this includes the character
    // layers, because the sight line to a body ends inside that body and hits
    // it on the way - so the answer is always "blocked" and the whole sense
    // reads as broken. The enemy's own vision draws the same distinction.
    [Tooltip("What blocks sight. Walls and doors, not characters.")]
    [SerializeField] private LayerMask obstructionMask = DefaultObstruction;

    private const int DefaultObstruction =
        (1 << 0) | (1 << 7) | (1 << 8) | (1 << 9);

    private readonly NetworkVariable<float> pitch = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    // Eyes sit near the top of a head, not at the very crown.
    private const float EyeShareOfHeight = 0.92f;

    private IReplicatedPlayerHidingStateService hidingState;
    private Camera ownerCamera;
    private CapsuleCollider cachedCapsule;
    private float lastSentPitch;

    public static IReadOnlyList<PlayerGazeNetwork> All => RegisteredGazes;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegisteredGazes()
    {
        RegisteredGazes.Clear();
    }

    // Taken from the capsule when there is one, so crouching lowers the eye
    // without this component having to hear about postures. The serialized
    // height is the fallback for anything that is not built that way.
    public Vector3 EyePosition
    {
        get
        {
            CapsuleCollider capsule = Capsule;

            float height = capsule != null
                ? capsule.center.y + capsule.height * 0.5f * EyeShareOfHeight
                : eyeHeight;

            return transform.position + Vector3.up * height;
        }
    }

    private CapsuleCollider Capsule
    {
        get
        {
            if (cachedCapsule == null)
            {
                cachedCapsule = GetComponent<CapsuleCollider>();
            }

            return cachedCapsule;
        }
    }

    // Body yaw from the network transform, pitch from the one value this
    // component sends.
    // Clamped on read. The owner writes this value, so a client could send
    // anything; the clamp bounds the nonsense to angles a camera can actually
    // reach. It cannot stop a client lying within that range - that would need
    // the server to own aiming, which is a much larger change than this.
    public Vector3 GazeDirection =>
        Quaternion.AngleAxis(
            Mathf.Clamp(pitch.Value, -MaxPitch, MaxPitch),
            transform.right) * transform.forward;

    private const float MaxPitch = 85f;

    public override void OnNetworkSpawn()
    {
        if (!RegisteredGazes.Contains(this))
        {
            RegisteredGazes.Add(this);
        }
    }

    public override void OnNetworkDespawn()
    {
        RegisteredGazes.Remove(this);
    }

    private void LateUpdate()
    {
        if (!IsOwner || !IsSpawned)
        {
            return;
        }

        if (ownerCamera == null)
        {
            ownerCamera = GetComponentInChildren<Camera>(true);

            if (ownerCamera == null)
            {
                return;
            }
        }

        // Signed angle between the body's flat forward and where the camera
        // actually points, which is the part the body rotation does not carry.
        float nextPitch = Vector3.SignedAngle(
            transform.forward,
            ownerCamera.transform.forward,
            transform.right
        );

        // A pitch that trickles out every frame is a message per frame per
        // player for something nobody can see moving. Only meaningful turns
        // are worth the traffic.
        if (Mathf.Abs(Mathf.DeltaAngle(lastSentPitch, nextPitch)) <
            PitchSendThreshold)
        {
            return;
        }

        lastSentPitch = nextPitch;
        pitch.Value = nextPitch;
    }

    // Someone folded into a box is not watching the room. The enemy's own
    // sight already works this way - a hidden player cannot be detected - and
    // without the same rule here a player in a crate scared the enemy off a
    // flank it should have walked straight past.
    public bool IsWatching =>
        HidingState == null || !HidingState.IsHidden;

    private IReplicatedPlayerHidingStateService HidingState
    {
        get
        {
            hidingState ??= GetComponent<IReplicatedPlayerHidingStateService>();
            return hidingState;
        }
    }

    public bool CanSee(Vector3 worldPoint)
    {
        if (!IsWatching)
        {
            return false;
        }

        Vector3 eye = EyePosition;
        Vector3 toPoint = worldPoint - eye;
        float distance = toPoint.magnitude;

        if (distance > maxViewDistance || distance <= 0.001f)
        {
            return false;
        }

        if (Vector3.Angle(GazeDirection, toPoint) > halfViewAngle)
        {
            return false;
        }

        return !Physics.Raycast(
            eye,
            toPoint / distance,
            distance,
            obstructionMask,
            QueryTriggerInteraction.Ignore
        );
    }

#if UNITY_EDITOR
    // Drawn from inside the component rather than from a debug one of its own:
    // the player assembly cannot see the enemy assembly's debug helpers, and
    // this is not worth a second component to get round that.
    [Header("Debug")]
    [SerializeField] private bool drawViewCone = true;

    private void OnDrawGizmosSelected()
    {
        if (!drawViewCone)
        {
            return;
        }

        Vector3 eye = EyePosition;
        Vector3 forward = GazeDirection;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 up = Vector3.Cross(forward, right);

        Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.9f);
        Gizmos.DrawRay(eye, forward * maxViewDistance);

        for (int i = 0; i < 4; i++)
        {
            Vector3 axis = i switch
            {
                0 => right,
                1 => -right,
                2 => up,
                _ => -up,
            };

            Vector3 edge = Quaternion.AngleAxis(halfViewAngle, Vector3.Cross(forward, axis))
                * forward;

            Gizmos.DrawRay(eye, edge * maxViewDistance);
        }
    }
#endif

    // A body is not a point. Asking about one spot on it means a head over a
    // crate reads as hidden and a foot under a railing reads as exposed,
    // depending on which spot was chosen. The enemy's own vision samples a
    // handful of points on its target for the same reason; this is that,
    // pointed the other way.
    public bool CanSeeBody(Vector3 footPosition, float height)
    {
        // One cheap rejection for the whole body before any raycasting. Three
        // probes each cast a ray, and the common answer is "nowhere near
        // looking at it" - which distance and angle settle on their own.
        if (!IsWatching || !IsBodyWorthCasting(footPosition, height))
        {
            return false;
        }

        return CanSee(footPosition + Vector3.up * (height * 0.95f)) ||
               CanSee(footPosition + Vector3.up * (height * 0.55f)) ||
               CanSee(footPosition + Vector3.up * (height * 0.15f));
    }

    // The question the enemy actually asks. Any player counts: being watched
    // by one is being watched.
    public static bool IsPointSeenByAnyone(Vector3 worldPoint)
    {
        for (int i = 0; i < RegisteredGazes.Count; i++)
        {
            PlayerGazeNetwork gaze = RegisteredGazes[i];

            if (gaze != null && gaze.CanSee(worldPoint))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsBodyWorthCasting(Vector3 footPosition, float height)
    {
        Vector3 centre = footPosition + Vector3.up * (height * 0.5f);
        Vector3 toCentre = centre - EyePosition;
        float distance = toCentre.magnitude;

        if (distance > maxViewDistance + height)
        {
            return false;
        }

        // Widened by however much of the body could stick out past its middle,
        // so a head just inside the cone is not thrown away by a test aimed at
        // the waist.
        float slack = Mathf.Atan2(height * 0.5f, Mathf.Max(distance, 0.01f)) *
                      Mathf.Rad2Deg;

        return Vector3.Angle(GazeDirection, toCentre) <= halfViewAngle + slack;
    }

    public static bool IsBodySeenByAnyone(Vector3 footPosition, float height)
    {
        for (int i = 0; i < RegisteredGazes.Count; i++)
        {
            PlayerGazeNetwork gaze = RegisteredGazes[i];

            if (gaze != null && gaze.CanSeeBody(footPosition, height))
            {
                return true;
            }
        }

        return false;
    }

    // Everyone who can see this body, not merely whether anybody can.
    //
    // Costs the same as the question above in the common case - the cheap
    // rejection runs first for every player either way - and it is the only
    // form a retreat or a flank can plan honestly against, because it can
    // route away from all of them at once instead of away from a guess.
    public static void CollectBodyWatchers(
        Vector3 footPosition,
        float height,
        List<PlayerWatcher> watchers
    )
    {
        if (watchers == null)
        {
            return;
        }

        watchers.Clear();

        for (int i = 0; i < RegisteredGazes.Count; i++)
        {
            PlayerGazeNetwork gaze = RegisteredGazes[i];

            if (gaze != null && gaze.CanSeeBody(footPosition, height))
            {
                watchers.Add(gaze.AsWatcher());
            }
        }
    }

    private PlayerWatcher AsWatcher()
    {
        return new PlayerWatcher(
            OwnerClientId,
            transform.position,
            EyePosition,
            GazeDirection
        );
    }
}
