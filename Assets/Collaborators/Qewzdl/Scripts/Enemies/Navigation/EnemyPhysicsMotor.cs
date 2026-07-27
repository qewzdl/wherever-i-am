using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Rigidbody))]
public sealed class EnemyPhysicsMotor : MonoBehaviour
{
    private const float MinimumDirectionSqrMagnitude = 0.0001f;

    [Header("References")]
    [SerializeField] private NetworkObject networkObject;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private Rigidbody body;
    [SerializeField] private EnemyItemPusher itemPusher;

    [Header("Physics")]
    [SerializeField, Min(0.01f)] private float mass = 30f;
    [SerializeField, Min(0f)] private float verticalCorrectionSpeed = 6f;
    [SerializeField, Min(0f)] private float maximumDepenetrationSpeed = 4f;

    [Header("Item Pushing")]
    [SerializeField, Min(0.01f)]
    private float pushResistanceCapacity = 10f;
    [SerializeField, Range(0.05f, 1f)]
    private float minimumPushSpeedMultiplier = 0.25f;

    private IEnemyDirectMovementIntentSource directMovementIntentSource;
    private bool controlsAgentMotion;
    private bool previousUpdatePosition;
    private bool previousUpdateRotation;

    public bool IsDrivingServerBody => controlsAgentMotion;
    public float CurrentPushSpeedMultiplier { get; private set; } = 1f;

    private void Awake()
    {
        CacheComponents();
        ConfigurePassiveBody();
    }

    private void FixedUpdate()
    {
        if (!CanDriveServerBody())
        {
            StopDriving();
            return;
        }

        StartDriving();
        DriveBody();
    }

    private void LateUpdate()
    {
        if (!controlsAgentMotion ||
            IsDirectMovementActive() ||
            agent == null ||
            !agent.enabled ||
            !agent.isOnNavMesh ||
            body == null)
        {
            return;
        }

        Vector3 nextPosition = agent.nextPosition;
        nextPosition.x = body.position.x;
        nextPosition.z = body.position.z;
        agent.nextPosition = nextPosition;
    }

    private bool CanDriveServerBody()
    {
        NetworkManager manager = networkObject != null
            ? networkObject.NetworkManager
            : null;
        bool hasDirectMovement = IsDirectMovementActive();
        bool hasNavMeshMovement =
            agent != null &&
            agent.enabled &&
            agent.isOnNavMesh;

        return networkObject != null &&
               networkObject.IsSpawned &&
               manager != null &&
               manager.IsServer &&
               body != null &&
               (hasDirectMovement || hasNavMeshMovement);
    }

    private void StartDriving()
    {
        if (controlsAgentMotion)
        {
            return;
        }

        previousUpdatePosition = agent.updatePosition;
        previousUpdateRotation = agent.updateRotation;
        agent.updatePosition = false;
        agent.updateRotation = false;

        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.nextPosition = body.position;
        }

        body.mass = Mathf.Max(0.01f, mass);
        body.useGravity = false;
        body.constraints =
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.detectCollisions = true;
        body.maxDepenetrationVelocity = Mathf.Max(
            0f,
            maximumDepenetrationSpeed
        );
        body.isKinematic = false;
        body.WakeUp();

        controlsAgentMotion = true;
    }

    private void DriveBody()
    {
        bool hasMovement = TryGetDesiredHorizontalVelocity(
            out Vector3 desiredVelocity,
            out bool usesDirectMovement
        );
        Vector3 currentHorizontalVelocity = Vector3.ProjectOnPlane(
            body.linearVelocity,
            Vector3.up
        );
        CurrentPushSpeedMultiplier = usesDirectMovement
            ? GetPushSpeedMultiplier()
            : 1f;
        desiredVelocity *= CurrentPushSpeedMultiplier;
        float acceleration = agent != null
            ? Mathf.Max(0f, agent.acceleration)
            : 0f;
        acceleration *= CurrentPushSpeedMultiplier;
        Vector3 horizontalVelocity = hasMovement
            ? Vector3.MoveTowards(
                currentHorizontalVelocity,
                desiredVelocity,
                acceleration * Time.fixedDeltaTime
            )
            : Vector3.zero;
        float verticalVelocity = 0f;

        if (!usesDirectMovement &&
            verticalCorrectionSpeed > 0f &&
            agent != null &&
            agent.enabled &&
            agent.isOnNavMesh)
        {
            verticalVelocity = Mathf.Clamp(
                (agent.nextPosition.y - body.position.y) /
                Mathf.Max(Time.fixedDeltaTime, 0.0001f),
                -verticalCorrectionSpeed,
                verticalCorrectionSpeed
            );
        }

        body.linearVelocity = new Vector3(
            horizontalVelocity.x,
            verticalVelocity,
            horizontalVelocity.z
        );
        body.angularVelocity = Vector3.zero;

        Vector3 facingDirection = usesDirectMovement
            ? desiredVelocity
            : horizontalVelocity.sqrMagnitude >= MinimumDirectionSqrMagnitude
                ? horizontalVelocity
                : desiredVelocity;

        if (facingDirection.sqrMagnitude < MinimumDirectionSqrMagnitude)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(
            facingDirection,
            Vector3.up
        );
        float angularSpeed = agent != null
            ? Mathf.Max(0f, agent.angularSpeed)
            : 0f;
        Quaternion nextRotation = Quaternion.RotateTowards(
            body.rotation,
            targetRotation,
            angularSpeed * Time.fixedDeltaTime
        );
        body.MoveRotation(nextRotation);
    }

    private bool TryGetDesiredHorizontalVelocity(
        out Vector3 desiredVelocity,
        out bool usesDirectMovement)
    {
        if (TryGetDirectMovementIntent(
                out Vector3 destination,
                out float speed))
        {
            desiredVelocity = destination - transform.position;
            desiredVelocity.y = 0f;
            usesDirectMovement = true;

            if (desiredVelocity.sqrMagnitude <
                MinimumDirectionSqrMagnitude)
            {
                desiredVelocity = Vector3.zero;
                return false;
            }

            desiredVelocity.Normalize();
            desiredVelocity *= Mathf.Max(0f, speed);
            return true;
        }

        usesDirectMovement = false;
        bool hasNavigationPath =
            agent != null &&
            agent.enabled &&
            agent.isOnNavMesh &&
            !agent.isStopped &&
            agent.hasPath;
        desiredVelocity = hasNavigationPath
            ? agent.desiredVelocity
            : Vector3.zero;
        desiredVelocity.y = 0f;
        return hasNavigationPath;
    }

    private bool IsDirectMovementActive()
    {
        return TryGetDirectMovementIntent(out _, out _);
    }

    private bool TryGetDirectMovementIntent(
        out Vector3 destination,
        out float speed)
    {
        if (directMovementIntentSource == null)
        {
            CacheDirectMovementIntentSource();
        }

        if (directMovementIntentSource != null &&
            directMovementIntentSource.TryGetEnemyDirectMovementIntent(
                out destination,
                out speed))
        {
            return true;
        }

        destination = default;
        speed = 0f;
        return false;
    }

    private void StopDriving()
    {
        if (body != null && !body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }

        if (!controlsAgentMotion)
        {
            return;
        }

        if (agent != null)
        {
            agent.updatePosition = previousUpdatePosition;
            agent.updateRotation = previousUpdateRotation;
        }

        controlsAgentMotion = false;
        CurrentPushSpeedMultiplier = 1f;
    }

    private void ConfigurePassiveBody()
    {
        if (body == null)
        {
            return;
        }

        body.mass = Mathf.Max(0.01f, mass);
        body.useGravity = false;
        body.constraints =
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.detectCollisions = true;
        body.maxDepenetrationVelocity = Mathf.Max(
            0f,
            maximumDepenetrationSpeed
        );
        body.isKinematic = true;
    }

    private void CacheComponents()
    {
        if (networkObject == null)
        {
            networkObject = GetComponent<NetworkObject>();
        }

        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
        }

        if (body == null)
        {
            body = GetComponent<Rigidbody>();
        }

        if (itemPusher == null)
        {
            itemPusher = GetComponent<EnemyItemPusher>();
        }

        CacheDirectMovementIntentSource();
    }

    private float GetPushSpeedMultiplier()
    {
        float resistance = itemPusher != null
            ? itemPusher.CurrentPushResistance
            : 0f;

        return CalculatePushSpeedMultiplier(
            resistance,
            pushResistanceCapacity,
            minimumPushSpeedMultiplier);
    }

    internal static float CalculatePushSpeedMultiplier(
        float resistance,
        float capacity,
        float minimumMultiplier)
    {
        float safeResistance = Mathf.Max(0f, resistance);
        float safeCapacity = Mathf.Max(0.01f, capacity);
        float safeMinimum = Mathf.Clamp(minimumMultiplier, 0.05f, 1f);
        float multiplier =
            safeCapacity / (safeCapacity + safeResistance);

        return Mathf.Clamp(multiplier, safeMinimum, 1f);
    }

    private void CacheDirectMovementIntentSource()
    {
        MonoBehaviour[] components = GetComponents<MonoBehaviour>();

        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] is IEnemyDirectMovementIntentSource source)
            {
                directMovementIntentSource = source;
                return;
            }
        }

        directMovementIntentSource = null;
    }

    private void OnDisable()
    {
        StopDriving();
    }

    private void OnDestroy()
    {
        StopDriving();
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();
        ConfigurePassiveBody();
    }

    private void OnValidate()
    {
        mass = Mathf.Max(0.01f, mass);
        verticalCorrectionSpeed = Mathf.Max(0f, verticalCorrectionSpeed);
        maximumDepenetrationSpeed = Mathf.Max(
            0f,
            maximumDepenetrationSpeed
        );
        pushResistanceCapacity = Mathf.Max(
            0.01f,
            pushResistanceCapacity);
        minimumPushSpeedMultiplier = Mathf.Clamp(
            minimumPushSpeedMultiplier,
            0.05f,
            1f);
        CacheComponents();
    }
#endif
}
