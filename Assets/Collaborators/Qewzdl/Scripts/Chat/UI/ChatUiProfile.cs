using UnityEngine;

[CreateAssetMenu(fileName = "ChatUiProfile", menuName = "Wherever I Am/Chat/UI Profile")]
public class ChatUiProfile : ScriptableObject
{
    [Header("Events")]
    [SerializeField] private ChatEventChannel chatEvents;

    [Header("Lobby Chat")]
    [SerializeField] private SoundEffect lobbyInputSfx;
    [SerializeField] private SoundEffect lobbyMessageWhileChatClosedSfx;
    [SerializeField] private SoundEffect lobbyMessageWhileChatOpenSfx;
    [SerializeField] private ChatTypographyProfile lobbyTypography;
    [SerializeField] private bool playLobbyMessageSfxForOwnMessages;
    [SerializeField] private bool playLobbyMessageSfxForSystemMessages = true;

    [Header("Phone Chat")]
    [SerializeField] private SoundEffect phoneInputSfx;
    [SerializeField] private SoundEffect phoneOpenSfx;
    [SerializeField] private SoundEffect phoneCloseSfx;
    [SerializeField] private SoundEffect incomingWhenClosedSfx;
    [SerializeField] private SoundEffect incomingWhenOpenedSfx;
    [SerializeField] private ChatTypographyProfile phoneTypography;
    [SerializeField] private PhoneSpriteAnimationProfile phoneAnimation;
    [SerializeField] private bool playPhoneIncomingSfxForOwnMessages;
    [SerializeField] private bool playPhoneIncomingSfxForSystemMessages = true;

    [Header("Phone Gameplay")]
    [SerializeField] private PhoneAudioCueEventChannel phoneAudioCueEvents;

    public ChatEventChannel ChatEvents => chatEvents;
    public SoundEffect LobbyInputSfx => lobbyInputSfx;
    public SoundEffect LobbyMessageWhileChatClosedSfx => lobbyMessageWhileChatClosedSfx;
    public SoundEffect LobbyMessageWhileChatOpenSfx => lobbyMessageWhileChatOpenSfx;
    public ChatTypographyProfile LobbyTypography => lobbyTypography;
    public bool PlayLobbyMessageSfxForOwnMessages => playLobbyMessageSfxForOwnMessages;
    public bool PlayLobbyMessageSfxForSystemMessages => playLobbyMessageSfxForSystemMessages;
    public PhoneSpriteAnimationProfile PhoneAnimation => phoneAnimation;
    public SoundEffect PhoneInputSfx => phoneInputSfx;
    public SoundEffect PhoneOpenSfx => phoneOpenSfx;
    public SoundEffect PhoneCloseSfx => phoneCloseSfx;
    public SoundEffect IncomingWhenClosedSfx => incomingWhenClosedSfx;
    public SoundEffect IncomingWhenOpenedSfx => incomingWhenOpenedSfx;
    public ChatTypographyProfile PhoneTypography => phoneTypography;
    public bool PlayPhoneIncomingSfxForOwnMessages => playPhoneIncomingSfxForOwnMessages;
    public bool PlayPhoneIncomingSfxForSystemMessages => playPhoneIncomingSfxForSystemMessages;
    public PhoneAudioCueEventChannel PhoneAudioCueEvents => phoneAudioCueEvents;
}
