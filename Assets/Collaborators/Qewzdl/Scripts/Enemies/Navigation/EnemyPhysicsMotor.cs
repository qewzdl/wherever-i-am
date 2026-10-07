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

    private readonly RaycastHit[] footingHits = new RaycastHit[8];
    private Collider[] bodyColliders;
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
        DriveBody();
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
               body != null &&
               agent != null &&
               agent.enabled &&
               agent.isOnNavMesh;
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
        EnemyPlayerContacts.AddEnemy(body);

        controlsAgentMotion = true;
    }

    private void DriveBody()
    {
        bool hasMovement = TryGetDesiredHorizontalVelocity(out Vector3 desiredVelocity);
        Vector3 currentHorizontalVelocity = Vector3.ProjectOnPlane(
            body.linearVelocity,
            Vector3.up
        );
        float acceleration = agent != null
            ? Mathf.Max(0f, agent.acceleration)
            : 0f;
        Vector3 horizontalVelocity = hasMovement
            ? Vector3.MoveTowards(
                currentHorizontalVelocity,
                desiredVelocity,
                acceleration * Time.fixedDeltaTime
            )
            : Vector3.zero;
        float verticalVelocity = 0f;

        if (verticalCorrectionSpeed > 0f &&
            agent != null &&
            agent.enabled &&
            agent.isOnNavMesh)
        {
            float targetHeight = agent.nextPosition.y;

            if (TryFindFooting(horizontalVelocity, out float footing))
                targetHeight = Mathf.Max(targetHeight, footing);

            verticalVelocity = Mathf.Clamp(
                (targetHeight - body.position.y) /
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

        Vector3 facingDirection = horizontalVelocity.sqrMagnitude >= MinimumDirectionSqrMagnitude
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

    // The ground she stands on, as the box of her feet does rather than as
    // the navmesh says - flat ground under the box, or just past its front
    // the way she is going, no higher than her agent climbs.
    //
    // Ahead: the navmesh follows the floor stair by stair and gives the
    // height of a stair only once her middle is over it, and the box of her
    // feet met the face of the first one before then - she never got up a
    // flight. Under her: going down, the navmesh dropped to the next stair
    // while the back of the box was still on the one above, she was pulled
    // down onto its edge, held there by it, and never got down one - nor off
    // the top of a flight.
    //
    // Only ahead and under: what she passes beside is not under her.
    // (Whether the navmesh is on it could not be asked: over stairs it lies
    // as a slope through their edges, a good way off most treads.)
    private bool TryFindFooting(Vector3 horizontalVelocity, out float footing)
    {
        footing = float.NegativeInfinity;
        float climb = NavMesh.GetSettingsByID(agent.agentTypeID).agentClimb;
        float feet = body.position.y;
        float footReach = FootReach();

        FootingAt(body.position, feet, climb, ref footing);

        for (int i = 0; i < 8; i++)
        {
            Vector3 under = Quaternion.AngleAxis(i * 45f, Vector3.up) * Vector3.forward * (footReach * 0.7f);
            FootingAt(body.position + under, feet, climb, ref footing);
        }

        float speed = horizontalVelocity.magnitude;

        if (speed >= 0.01f)
        {
            Vector3 heading = horizontalVelocity / speed;
            float reach = footReach + 0.03f + speed * Time.fixedDeltaTime * 2f;

            for (int i = 0; i < 5; i++)
                FootingAt(body.position + Quaternion.AngleAxis(-45f + i * 22.5f, Vector3.up) * heading * reach, feet, climb, ref footing);
        }

        return !float.IsNegativeInfinity(footing);
    }

    private void FootingAt(Vector3 at, float feet, float climb, ref float footing)
    {
        at.y = feet + climb + 0.05f;

        if (TryFindStaticGround(at, climb + 0.1f, out RaycastHit ground) &&
            ground.normal.y > 0.95f &&
            ground.point.y > footing)
        {
            footing = ground.point.y;
        }
    }

    private bool TryFindStaticGround(Vector3 origin, float distance, out RaycastHit nearest)
    {
        nearest = default;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, footingHits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = footingHits[i];

            if (hit.collider == null || hit.collider.attachedRigidbody != null)
                continue;

            if (!found || hit.distance < nearest.distance)
            {
                nearest = hit;
                found = true;
            }
        }

        return found;
    }

    // How far out from her middle her feet reach, at most.
    private float FootReach()
    {
        bodyColliders ??= body.GetComponentsInChildren<Collider>();
        float reach = 0f;

        foreach (Collider part in bodyColliders)
        {
            if (part == null || part.isTrigger || !part.enabled)
                continue;

            Bounds bounds = part.bounds;
            Vector3 offset = bounds.center - body.position;
            offset.y = 0f;
            reach = Mathf.Max(reach, offset.magnitude + new Vector2(bounds.extents.x, bounds.extents.z).magnitude);
        }

        return reach;
    }

    private bool TryGetDesiredHorizontalVelocity(out Vector3 desiredVelocity)
    {
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

    private void StopDriving()
    {
        EnemyPlayerContacts.RemoveEnemy(body);

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
