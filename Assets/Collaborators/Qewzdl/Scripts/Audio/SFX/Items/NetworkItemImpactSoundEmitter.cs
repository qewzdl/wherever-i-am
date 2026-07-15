using System;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public sealed class NetworkItemImpactSoundEmitter : NetworkBehaviour, IGameplaySoundServiceConsumer
{
    private const ulong NoPredictedClientId = ulong.MaxValue;
    private const ulong NoFormerOwnerClientId = ulong.MaxValue;

    [Header("Config")]
    [SerializeField] private ItemImpactSoundProfile profile;
    [SerializeField] private Rigidbody sourceRigidbody;
    [SerializeField] private Transform soundOrigin;

    [Header("Client Report Validation")]
    [SerializeField, Min(0f)] private float maxClientReportDistance = 3f;
    [SerializeField, Min(0f)] private float maxAcceptedClientImpactSpeed = 35f;
    [SerializeField, Min(0f)] private float formerOwnerReportGracePeriod = 1.5f;

    private Vector3 previousVelocity;
    private bool hasVelocitySample;
    private float lastLocalReportTime = float.NegativeInfinity;
    private float lastServerRelayTime = float.NegativeInfinity;
    private ulong formerOwnerClientId = NoFormerOwnerClientId;
    private float ownershipChangedAtTime = float.NegativeInfinity;
    private IGameplaySoundService gameplaySoundService;

    public event Action<Vector3, ItemImpactSoundId> ServerImpactAccepted;

    private void Awake()
    {
        CacheComponents();
    }

    public override void OnNetworkSpawn()
    {
        if (NetworkObjectServiceContext.TryResolveSessionService(
                NetworkManager,
                out IAudioService audioService))
        {
            Construct(audioService.Gameplay);
        }

        if (TryGetComponent(out NetworkItemImpactGameplayNoiseEmitter noiseEmitter) &&
            NetworkObjectServiceContext.TryResolveSessionService(
                NetworkManager,
                out IGameplayNoiseService noiseService))
        {
            noiseEmitter.Construct(noiseService);
        }

        ResetRuntimeState();
    }

    public override void OnNetworkDespawn()
    {
        ResetRuntimeState();
        ReleaseGameplaySoundService();

        if (TryGetComponent(out NetworkItemImpactGameplayNoiseEmitter noiseEmitter))
            noiseEmitter.ReleaseGameplayNoiseService();
    }

    public void Construct(IGameplaySoundService service)
    {
        gameplaySoundService = service;
    }

    public void ReleaseGameplaySoundService()
    {
        gameplaySoundService = null;
    }

    protected override void OnOwnershipChanged(ulong previous, ulong current)
    {
        base.OnOwnershipChanged(previous, current);

        if (!IsServer)
        {
            return;
        }

        formerOwnerClientId = previous;
        ownershipChangedAtTime = Time.time;
    }

    private void FixedUpdate()
    {
        if (sourceRigidbody == null)
        {
            return;
        }

        previousVelocity = sourceRigidbody.linearVelocity;
        hasVelocitySample = true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (IsNetworkActive() && !HasAuthority)
        {
            return;
        }

        if (IsPlayerCollision(collision))
        {
            return;
        }

        if (!TryBuildImpactReport(
                collision,
                out Vector3 position,
                out float impactSpeed,
                out float downwardSpeed,
                out bool hasLandingContact,
                out ItemImpactSoundId soundId))
        {
            return;
        }

        if (!CanPassLocalReportCooldown())
        {
            return;
        }

        lastLocalReportTime = Time.time;

        if (!IsNetworkActive())
        {
            PlayLocalImpact(position, soundId);
            return;
        }

        if (IsServer)
        {
            TryRelayImpactServer(
                position,
                impactSpeed,
                downwardSpeed,
                hasLandingContact,
                NoPredictedClientId);
            return;
        }

        PlayLocalImpact(position, soundId);
        ReportImpactSoundServerRpc(position, impactSpeed, downwardSpeed, hasLandingContact);
    }

    private static bool IsPlayerCollision(Collision collision)
    {
        Collider otherCollider = collision.collider;

        return otherCollider != null &&
               (otherCollider.GetComponentInParent<PlayerNetwork>() != null ||
                otherCollider.GetComponentInParent<PlayerController>() != null);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ReportImpactSoundServerRpc(
        Vector3 reportedPosition,
        float reportedImpactSpeed,
        float reportedDownwardSpeed,
        bool reportedLandingContact,
        RpcParams rpcParams = default)
    {
        if (!TryValidateClientReport(
                reportedPosition,
                reportedImpactSpeed,
                reportedDownwardSpeed,
                rpcParams.Receive.SenderClientId,
                out float impactSpeed,
                out float downwardSpeed))
        {
            return;
        }

        TryRelayImpactServer(
            reportedPosition,
            impactSpeed,
            downwardSpeed,
            reportedLandingContact,
            rpcParams.Receive.SenderClientId);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void PlayImpactSoundClientRpc(
        Vector3 position,
        byte soundIdValue,
        ulong predictedClientId)
    {
        if (predictedClientId != NoPredictedClientId &&
            NetworkManager != null &&
            NetworkManager.LocalClientId == predictedClientId)
        {
            return;
        }

        PlayLocalImpact(position, (ItemImpactSoundId)soundIdValue);
    }

    private bool TryBuildImpactReport(
        Collision collision,
        out Vector3 position,
        out float impactSpeed,
        out float downwardSpeed,
        out bool hasLandingContact,
        out ItemImpactSoundId soundId)
    {
        position = GetCollisionPosition(collision);
        Vector3 velocityBeforeImpact = GetVelocityBeforeImpact();
        soundId = ItemImpactSoundId.None;

        impactSpeed = Mathf.Max(
            collision.relativeVelocity.magnitude,
            velocityBeforeImpact.magnitude);
        downwardSpeed = Mathf.Max(0f, -velocityBeforeImpact.y);
        hasLandingContact = HasLandingContact(collision);

        return profile != null &&
               profile.TryResolveSound(
                   impactSpeed,
                   downwardSpeed,
                   hasLandingContact,
                   out soundId);
    }

    private bool TryRelayImpactServer(
        Vector3 position,
        float impactSpeed,
        float downwardSpeed,
        bool hasLandingContact,
        ulong predictedClientId)
    {
        if (!IsServer ||
            profile == null ||
            !profile.TryResolveSound(
                impactSpeed,
                downwardSpeed,
                hasLandingContact,
                out ItemImpactSoundId soundId))
        {
            return false;
        }

        if (!CanPassServerRelayCooldown())
        {
            return false;
        }

        lastServerRelayTime = Time.time;
        ServerImpactAccepted?.Invoke(position, soundId);
        PlayImpactSoundClientRpc(
            position,
            (byte)soundId,
            predictedClientId);
        return true;
    }

    private bool TryValidateClientReport(
        Vector3 reportedPosition,
        float reportedImpactSpeed,
        float reportedDownwardSpeed,
        ulong senderClientId,
        out float impactSpeed,
        out float downwardSpeed)
    {
        impactSpeed = 0f;
        downwardSpeed = 0f;

        if (!IsServer ||
            profile == null ||
            !profile.HasAnySound ||
            !IsFinite(reportedPosition) ||
            !IsFinite(reportedImpactSpeed) ||
            !IsFinite(reportedDownwardSpeed) ||
            !IsAuthorizedReporter(senderClientId))
        {
            return false;
        }

        impactSpeed = ClampReportedSpeed(reportedImpactSpeed);
        downwardSpeed = ClampReportedSpeed(reportedDownwardSpeed);

        if (impactSpeed < profile.MinimumImpactSpeed)
        {
            return false;
        }

        return IsReportedPositionNearItem(reportedPosition);
    }

    private void PlayLocalImpact(
        Vector3 position,
        ItemImpactSoundId soundId)
    {
        if (profile == null ||
            !profile.TryGetSound(soundId, out SoundEffect sound))
        {
            return;
        }

        if (gameplaySoundService == null)
        {
            return;
        }

        gameplaySoundService.PlayAtPosition(sound, position);
    }

    private bool CanPassLocalReportCooldown()
    {
        return CanPassCooldown(lastLocalReportTime);
    }

    private bool CanPassServerRelayCooldown()
    {
        return CanPassCooldown(lastServerRelayTime);
    }

    private bool CanPassCooldown(float lastTime)
    {
        float cooldown = profile != null ? profile.Cooldown : 0f;
        return cooldown <= 0f || Time.time - lastTime >= cooldown;
    }

    private bool IsNetworkActive()
    {
        return NetworkManager != null &&
               NetworkManager.IsListening &&
               IsSpawned;
    }

    private bool IsReportedPositionNearItem(Vector3 reportedPosition)
    {
        float maxDistance = Mathf.Max(0f, maxClientReportDistance);

        if (maxDistance <= 0f)
        {
            return true;
        }

        Vector3 referencePosition = soundOrigin != null
            ? soundOrigin.position
            : transform.position;

        return Vector3.Distance(referencePosition, reportedPosition) <= maxDistance;
    }

    private float ClampReportedSpeed(float speed)
    {
        float maxSpeed = Mathf.Max(0f, maxAcceptedClientImpactSpeed);

        if (maxSpeed <= 0f)
        {
            return Mathf.Max(0f, speed);
        }

        return Mathf.Clamp(speed, 0f, maxSpeed);
    }

    private bool IsAuthorizedReporter(ulong senderClientId)
    {
        if (senderClientId == NetworkManager.ServerClientId)
        {
            return false;
        }

        if (senderClientId == OwnerClientId)
        {
            return true;
        }

        float gracePeriod = Mathf.Max(0f, formerOwnerReportGracePeriod);

        return gracePeriod > 0f &&
               senderClientId == formerOwnerClientId &&
               Time.time - ownershipChangedAtTime <= gracePeriod;
    }

    private Vector3 GetCollisionPosition(Collision collision)
    {
        int contactCount = collision.contactCount;

        if (contactCount <= 0)
        {
            return soundOrigin != null ? soundOrigin.position : transform.position;
        }

        Vector3 position = Vector3.zero;

        for (int i = 0; i < contactCount; i++)
        {
            position += collision.GetContact(i).point;
        }

        return position / contactCount;
    }

    private bool HasLandingContact(Collision collision)
    {
        if (profile == null)
        {
            return false;
        }

        float minimumNormalY = profile.MinimumLandingNormalY;

        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y >= minimumNormalY)
            {
                return true;
            }
        }

        return false;
    }

    private Vector3 GetVelocityBeforeImpact()
    {
        if (hasVelocitySample)
        {
            return previousVelocity;
        }

        return sourceRigidbody != null
            ? sourceRigidbody.linearVelocity
            : Vector3.zero;
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) &&
               IsFinite(value.y) &&
               IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void CacheComponents()
    {
        if (sourceRigidbody == null)
        {
            sourceRigidbody = GetComponent<Rigidbody>();
        }
    }

    private void ResetRuntimeState()
    {
        previousVelocity = Vector3.zero;
        hasVelocitySample = false;
        lastLocalReportTime = float.NegativeInfinity;
        lastServerRelayTime = float.NegativeInfinity;
        formerOwnerClientId = NoFormerOwnerClientId;
        ownershipChangedAtTime = float.NegativeInfinity;
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        CacheComponents();
        maxClientReportDistance = Mathf.Max(0f, maxClientReportDistance);
        maxAcceptedClientImpactSpeed = Mathf.Max(0f, maxAcceptedClientImpactSpeed);
        formerOwnerReportGracePeriod = Mathf.Max(0f, formerOwnerReportGracePeriod);
    }
#endif
}
