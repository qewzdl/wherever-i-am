using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class NetworkPhoneGameplayNoiseEmitter : NetworkBehaviour
{
    private const int MaxTrackedNotificationIds = 128;

    [Header("Events")]
    [SerializeField] private PhoneAudioCueEventChannel phoneAudioCueEvents;

    [Header("Profiles")]
    [SerializeField] private ChatUiProfile chatUiProfile;
    [SerializeField] private PhoneGameplayNoiseProfile noiseProfile;

    [Header("Noise")]
    [SerializeField] private GameplayNoiseEmitter noiseEmitter;

    private readonly HashSet<uint> processedNotificationIds = new();
    private readonly Queue<uint> processedNotificationOrder = new();

    private ISessionServiceRegistry serviceRegistry;
    private IChatReadService chatReadService;
    private bool isPhoneOpen;
    private bool invalidConfigurationLogged;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            ResetServerState();
            BindSessionServices();
            ValidateDependencies();
        }

        if (IsOwner)
        {
            Subscribe();
        }
    }

    public override void OnNetworkDespawn()
    {
        Unsubscribe();
        UnbindSessionServices();
        ResetServerState();
    }

    public override void OnDestroy()
    {
        Unsubscribe();
        UnbindSessionServices();
        base.OnDestroy();
    }

    private void Subscribe()
    {
        if (phoneAudioCueEvents == null)
        {
            ValidateDependencies();
            return;
        }

        phoneAudioCueEvents.CuePlayed -= HandleCuePlayed;
        phoneAudioCueEvents.CuePlayed += HandleCuePlayed;
    }

    private void Unsubscribe()
    {
        if (phoneAudioCueEvents != null)
        {
            phoneAudioCueEvents.CuePlayed -= HandleCuePlayed;
        }
    }

    private void HandleCuePlayed(PhoneAudioCueEvent cueEvent)
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        if (IsServer)
        {
            ProcessCueServer(
                cueEvent.CueType,
                cueEvent.MessageId,
                OwnerClientId
            );
            return;
        }

        ReportCueRpc(
            (byte)cueEvent.CueType,
            cueEvent.MessageId
        );
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void ReportCueRpc(
        byte cueTypeValue,
        uint messageId,
        RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        ProcessCueServer(
            (PhoneAudioCueType)cueTypeValue,
            messageId,
            rpcParams.Receive.SenderClientId
        );
    }

    private void ProcessCueServer(
        PhoneAudioCueType cueType,
        uint messageId,
        ulong senderClientId)
    {
        if (!IsServer ||
            !IsSpawned ||
            senderClientId != OwnerClientId ||
            !ValidateDependencies())
        {
            return;
        }

        switch (cueType)
        {
            case PhoneAudioCueType.IncomingNotification:
                TryEmitNotificationServer(messageId);
                break;

            case PhoneAudioCueType.Open:
                ApplyPhoneStateAndTryEmitInteractionServer(true);
                break;

            case PhoneAudioCueType.Close:
                ApplyPhoneStateAndTryEmitInteractionServer(false);
                break;

            case PhoneAudioCueType.Input:
                TryEmitInputServer();
                break;
        }
    }

    private void TryEmitNotificationServer(uint messageId)
    {
        if (messageId == 0 ||
            processedNotificationIds.Contains(messageId) ||
            !TryValidateNotificationMessage(messageId))
        {
            return;
        }

        TrackProcessedNotification(messageId);
        TryEmitNoiseServer(PhoneAudioCueType.IncomingNotification);
    }

    private bool TryValidateNotificationMessage(uint messageId)
    {
        if (chatReadService == null ||
            chatReadService.CurrentChannel != ChatChannel.Game ||
            !chatReadService.TryGetMessage(messageId, out ChatMessageData message))
        {
            return false;
        }

        bool isSystemMessage = message.Channel == ChatChannel.System;
        bool isLocalSender =
            !isSystemMessage &&
            message.SenderClientId == OwnerClientId;

        if (isLocalSender &&
            !chatUiProfile.PlayPhoneIncomingSfxForOwnMessages)
        {
            return false;
        }

        if (isSystemMessage &&
            !chatUiProfile.PlayPhoneIncomingSfxForSystemMessages)
        {
            return false;
        }

        SoundEffect configuredSound = isPhoneOpen
            ? chatUiProfile.IncomingWhenOpenedSfx
            : chatUiProfile.IncomingWhenClosedSfx;

        return configuredSound != null;
    }

    private void BindSessionServices()
    {
        UnbindSessionServices();

        if (NetworkManager == null)
            return;

        NetworkSessionOrchestrator orchestrator =
            NetworkManager.GetComponent<NetworkSessionOrchestrator>();

        if (orchestrator == null ||
            !orchestrator.TryGetSessionServiceRegistry(out serviceRegistry))
        {
            serviceRegistry = null;
            return;
        }

        serviceRegistry.ServicesChanged += RefreshChatService;
        RefreshChatService();
    }

    private void UnbindSessionServices()
    {
        if (serviceRegistry != null)
            serviceRegistry.ServicesChanged -= RefreshChatService;

        serviceRegistry = null;
        chatReadService = null;
    }

    private void RefreshChatService()
    {
        chatReadService = null;

        if (serviceRegistry != null && !serviceRegistry.IsDisposed)
            serviceRegistry.TryResolve(out chatReadService);
    }

    private void ApplyPhoneStateAndTryEmitInteractionServer(bool shouldOpen)
    {
        if (isPhoneOpen == shouldOpen)
        {
            return;
        }

        isPhoneOpen = shouldOpen;

        SoundEffect configuredSound = shouldOpen
            ? chatUiProfile.PhoneOpenSfx
            : chatUiProfile.PhoneCloseSfx;

        if (configuredSound == null)
        {
            return;
        }

        PhoneAudioCueType cueType = shouldOpen
            ? PhoneAudioCueType.Open
            : PhoneAudioCueType.Close;

        TryEmitNoiseServer(cueType);
    }

    private void TryEmitInputServer()
    {
        if (!isPhoneOpen ||
            chatUiProfile.PhoneInputSfx == null)
        {
            return;
        }

        TryEmitNoiseServer(PhoneAudioCueType.Input);
    }

    private bool TryEmitNoiseServer(PhoneAudioCueType cueType)
    {
        if (!noiseProfile.TryGetPreset(
                cueType,
                out GameplayNoisePreset preset))
        {
            return false;
        }

        return noiseEmitter.TryEmitServer(preset);
    }

    private void TrackProcessedNotification(uint messageId)
    {
        processedNotificationIds.Add(messageId);
        processedNotificationOrder.Enqueue(messageId);

        while (processedNotificationOrder.Count > MaxTrackedNotificationIds)
        {
            uint expiredMessageId = processedNotificationOrder.Dequeue();
            processedNotificationIds.Remove(expiredMessageId);
        }
    }

    private bool ValidateDependencies()
    {
        if (phoneAudioCueEvents != null &&
            chatUiProfile != null &&
            noiseProfile != null &&
            noiseEmitter != null &&
            noiseEmitter.IsConfigured)
        {
            invalidConfigurationLogged = false;
            return true;
        }

        if (!invalidConfigurationLogged)
        {
            invalidConfigurationLogged = true;

            Debug.LogError(
                $"{nameof(NetworkPhoneGameplayNoiseEmitter)} requires " +
                $"{nameof(PhoneAudioCueEventChannel)}, {nameof(ChatUiProfile)}, " +
                $"{nameof(PhoneGameplayNoiseProfile)} and configured " +
                $"{nameof(GameplayNoiseEmitter)}.",
                this
            );
        }

        return false;
    }

    private void ResetServerState()
    {
        isPhoneOpen = false;
        processedNotificationIds.Clear();
        processedNotificationOrder.Clear();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (noiseEmitter == null)
        {
            noiseEmitter = GetComponent<GameplayNoiseEmitter>();
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (noiseProfile == null ||
            !noiseProfile.TryGetPreset(
                PhoneAudioCueType.IncomingNotification,
                out GameplayNoisePreset preset))
        {
            return;
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, preset.Radius);
    }
#endif
}
