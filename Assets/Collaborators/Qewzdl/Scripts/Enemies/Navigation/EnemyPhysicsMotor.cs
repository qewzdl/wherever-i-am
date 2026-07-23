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

    [Header("Physics")]
    [SerializeField, Min(0.01f)] private float mass = 30f;
    [SerializeField, Min(0f)] private float verticalCorrectionSpeed = 6f;
    [SerializeField, Min(0f)] private float maximumDepenetrationSpeed = 4f;

    private bool controlsAgentMotion;
    private bool previousUpdatePosition;
    private bool previousUpdateRotation;

    public bool IsDrivingServerBody => controlsAgentMotion;

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
        DriveBodyFromNavigation();
    }

    private void LateUpdate()
    {
        if (!controlsAgentMotion ||
            agent == null ||
            !agent.enabled ||
            !agent.isOnNavMesh ||
            body == null)
        {
            return;
        }

        // Keep the navigation simulation attached to the collision-resolved body.
        // Preserve the agent's vertical NavMesh coordinate so slopes still produce
        // a vertical correction during the next physics tick.
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

        return networkObject != null &&
               networkObject.IsSpawned &&
               manager != null &&
               manager.IsServer &&
               agent != null &&
               agent.enabled &&
               agent.isOnNavMesh &&
               body != null;
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
        agent.nextPosition = body.position;

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

    private void DriveBodyFromNavigation()
    {
        bool hasMovement = !agent.isStopped && agent.hasPath;
        Vector3 desiredVelocity = hasMovement
            ? agent.desiredVelocity
            : Vector3.zero;
        desiredVelocity.y = 0f;

        Vector3 currentHorizontalVelocity = Vector3.ProjectOnPlane(
            body.linearVelocity,
            Vector3.up
        );
        float acceleration = Mathf.Max(0f, agent.acceleration);
        Vector3 horizontalVelocity = hasMovement
            ? Vector3.MoveTowards(
                currentHorizontalVelocity,
                desiredVelocity,
                acceleration * Time.fixedDeltaTime
            )
            : Vector3.zero;

        float verticalVelocity = 0f;

        if (verticalCorrectionSpeed > 0f)
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

        Vector3 facingDirection = horizontalVelocity.sqrMagnitude >=
                                  MinimumDirectionSqrMagnitude
            ? horizontalVelocity
            : desiredVelocity;

        if (facingDirection.sqrMagnitude >= MinimumDirectionSqrMagnitude)
        {
            Quaternion targetRotation = Quaternion.LookRotation(
                facingDirection,
                Vector3.up
            );
            Quaternion nextRotation = Quaternion.RotateTowards(
                body.rotation,
                targetRotation,
                Mathf.Max(0f, agent.angularSpeed) * Time.fixedDeltaTime
            );
            body.MoveRotation(nextRotation);
        }
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
        CacheComponents();
    }
#endif
}
