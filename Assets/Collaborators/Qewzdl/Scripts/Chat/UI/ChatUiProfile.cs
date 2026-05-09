using UnityEngine;

[CreateAssetMenu(fileName = "ChatUiProfile", menuName = "Chat/UI Profile")]
public class ChatUiProfile : ScriptableObject
{
    [Header("Events")]
    [SerializeField] private ChatEventChannel chatEvents;

    [Header("Lobby Chat")]
    [SerializeField] private SoundEffect lobbyInputSfx;
    [SerializeField] private SoundEffect lobbyMessageWhileChatClosedSfx;
    [SerializeField] private SoundEffect lobbyMessageWhileChatOpenSfx;
    [SerializeField] private bool playLobbyMessageSfxForOwnMessages;
    [SerializeField] private bool playLobbyMessageSfxForSystemMessages = true;

    [Header("Phone Chat")]
    [SerializeField] private SoundEffect phoneInputSfx;
    [SerializeField] private SoundEffect phoneOpenSfx;
    [SerializeField] private SoundEffect phoneCloseSfx;
    [SerializeField] private SoundEffect incomingWhenClosedSfx;
    [SerializeField] private SoundEffect incomingWhenOpenedSfx;
    [SerializeField] private bool playPhoneIncomingSfxForOwnMessages;
    [SerializeField] private bool playPhoneIncomingSfxForSystemMessages = true;

    public ChatEventChannel ChatEvents => chatEvents;
    public SoundEffect LobbyInputSfx => lobbyInputSfx;
    public SoundEffect LobbyMessageWhileChatClosedSfx => lobbyMessageWhileChatClosedSfx;
    public SoundEffect LobbyMessageWhileChatOpenSfx => lobbyMessageWhileChatOpenSfx;
    public bool PlayLobbyMessageSfxForOwnMessages => playLobbyMessageSfxForOwnMessages;
    public bool PlayLobbyMessageSfxForSystemMessages => playLobbyMessageSfxForSystemMessages;
    public SoundEffect PhoneInputSfx => phoneInputSfx;
    public SoundEffect PhoneOpenSfx => phoneOpenSfx;
    public SoundEffect PhoneCloseSfx => phoneCloseSfx;
    public SoundEffect IncomingWhenClosedSfx => incomingWhenClosedSfx;
    public SoundEffect IncomingWhenOpenedSfx => incomingWhenOpenedSfx;
    public bool PlayPhoneIncomingSfxForOwnMessages => playPhoneIncomingSfxForOwnMessages;
    public bool PlayPhoneIncomingSfxForSystemMessages => playPhoneIncomingSfxForSystemMessages;
}
