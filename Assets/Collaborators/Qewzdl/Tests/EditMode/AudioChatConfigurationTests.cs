using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

[Category("Presentation")]
public sealed class AudioChatConfigurationTests
{
    [Test]
    public void ChatEvents_NormalizeFallbackPresentationData()
    {
        ChatMessageReceivedEvent message = new(
            "",
            "",
            12,
            "",
            null,
            isLocalSender: false,
            isSystemMessage: false,
            serverTime: 4d);

        Assert.That(message.MessageId, Is.EqualTo("unknown"));
        Assert.That(message.ChannelId, Is.EqualTo("global"));
        Assert.That(message.SenderDisplayName, Is.EqualTo("Player 12"));
        Assert.That(message.Text, Is.Empty);

        ChatSendRejectedEvent rejected = new(default, "");
        Assert.That(rejected.Reason, Is.EqualTo("Message was rejected."));
    }

    [Test]
    public void UnreadEvent_ClampsCountsAndReportsDelta()
    {
        ChatUnreadCountChangedEvent unread = new(-4, 3);
        Assert.That(unread.PreviousUnreadCount, Is.Zero);
        Assert.That(unread.UnreadCount, Is.EqualTo(3));
        Assert.That(unread.Delta, Is.EqualTo(3));
        Assert.That(unread.HasUnreadMessages, Is.True);

        ChatUnreadCountChangedEvent cleared = new(3, -1);
        Assert.That(cleared.UnreadCount, Is.Zero);
        Assert.That(cleared.Delta, Is.EqualTo(-3));
        Assert.That(cleared.HasUnreadMessages, Is.False);
    }

    [Test]
    public void ChatEventChannel_FiltersBlankMessagesAndTracksUnreadState()
    {
        ChatEventChannel channel = ScriptableObject.CreateInstance<ChatEventChannel>();
        int received = 0;
        int unreadEvents = 0;
        channel.MessageReceived += _ => received++;
        channel.UnreadCountChanged += _ => unreadEvents++;

        try
        {
            channel.RaiseMessageReceived(new ChatMessageReceivedEvent(
                "1", "lobby", 1, "A", " ", false, false, 0d));
            channel.RaiseMessageReceived(new ChatMessageReceivedEvent(
                "2", "lobby", 1, "A", "hello", false, false, 0d));
            channel.RaiseUnreadCountChanged(-5);
            channel.RaiseUnreadCountChanged(4);

            Assert.That(received, Is.EqualTo(1));
            Assert.That(unreadEvents, Is.EqualTo(2));
            Assert.That(channel.CurrentUnreadCount, Is.EqualTo(4));
        }
        finally
        {
            Object.DestroyImmediate(channel);
        }
    }

    [Test]
    public void PhoneCueChannel_RejectsAmbiguousNotificationIdentity()
    {
        PhoneAudioCueEventChannel channel =
            ScriptableObject.CreateInstance<PhoneAudioCueEventChannel>();
        int received = 0;
        channel.CuePlayed += _ => received++;

        try
        {
            Assert.That(
                channel.RaiseCuePlayed(PhoneAudioCueEvent.IncomingNotification(0)),
                Is.False);
            Assert.That(
                channel.RaiseCuePlayed(PhoneAudioCueEvent.IncomingNotification(7)),
                Is.True);
            Assert.That(channel.RaiseCuePlayed(PhoneAudioCueEvent.Open()), Is.True);
            Assert.That(channel.RaiseCuePlayed(PhoneAudioCueEvent.Close()), Is.True);
            Assert.That(channel.RaiseCuePlayed(PhoneAudioCueEvent.Input()), Is.True);
            Assert.That(received, Is.EqualTo(4));
        }
        finally
        {
            Object.DestroyImmediate(channel);
        }
    }

    [Test]
    public void ChatConfig_ClampsStorageLengthAndCooldown()
    {
        ChatConfig config = ScriptableObject.CreateInstance<ChatConfig>();

        try
        {
            TestReflection.SetField(config, "maxStoredMessages", 0);
            TestReflection.SetField(config, "maxMessageLength", 1000);
            TestReflection.SetField(config, "messageCooldownSeconds", -3f);

            Assert.That(config.MaxStoredMessages, Is.EqualTo(1));
            Assert.That(config.MaxMessageLength, Is.EqualTo(240));
            Assert.That(config.MessageCooldownSeconds, Is.Zero);
        }
        finally
        {
            Object.DestroyImmediate(config);
        }
    }

    [Test]
    public void PhoneAnimationProfile_MapsOpeningAndReverseClosingFrames()
    {
        PhoneSpriteAnimationProfile profile =
            ScriptableObject.CreateInstance<PhoneSpriteAnimationProfile>();
        Sprite first = CreateSprite(Color.red);
        Sprite middle = CreateSprite(Color.green);
        Sprite last = CreateSprite(Color.blue);

        try
        {
            TestReflection.SetField(
                profile,
                "frames",
                new List<Sprite> { first, middle, last });
            TestReflection.SetField(profile, "framesPerSecond", 0f);
            TestReflection.SetField(profile, "playClosingInReverse", true);

            Assert.That(profile.FrameCount, Is.EqualTo(3));
            Assert.That(profile.FrameDuration, Is.EqualTo(1f));
            Assert.That(profile.ClosedSprite, Is.SameAs(first));
            Assert.That(profile.OpenedSprite, Is.SameAs(last));
            Assert.That(
                profile.GetFrame(PhoneSpriteAnimationDirection.Opening, 1),
                Is.SameAs(middle));
            Assert.That(
                profile.GetFrame(PhoneSpriteAnimationDirection.Closing, 0),
                Is.SameAs(last));
            Assert.That(
                profile.GetFrame(PhoneSpriteAnimationDirection.Closing, 99),
                Is.SameAs(first));
        }
        finally
        {
            DestroySprite(first);
            DestroySprite(middle);
            DestroySprite(last);
            Object.DestroyImmediate(profile);
        }
    }

    [Test]
    public void SoundEffect_UsesConfiguredClipAndSafeRandomRanges()
    {
        SoundEffect sound = ScriptableObject.CreateInstance<SoundEffect>();
        AudioClip clip = AudioClip.Create("test", 32, 1, 8000, false);

        try
        {
            TestReflection.SetField(sound, "clips", new[] { clip });
            TestReflection.SetField(sound, "volume", 0.8f);
            TestReflection.SetField(sound, "randomizeVolume", true);
            TestReflection.SetField(sound, "minVolume", 0.5f);
            TestReflection.SetField(sound, "maxVolume", 0.75f);
            TestReflection.SetField(sound, "randomizePitch", true);
            TestReflection.SetField(sound, "minPitch", 0.9f);
            TestReflection.SetField(sound, "maxPitch", 1.1f);

            Random.InitState(1234);
            Assert.That(sound.GetClip(), Is.SameAs(clip));
            Assert.That(sound.GetVolume(), Is.InRange(0.4f, 0.6f));
            Assert.That(sound.GetPitch(), Is.InRange(0.9f, 1.1f));
        }
        finally
        {
            Object.DestroyImmediate(sound);
            Object.DestroyImmediate(clip);
        }
    }

    [Test]
    public void MusicCueAndSceneRegistry_ResolveTracksProfilesAndFallback()
    {
        MusicTrack track = ScriptableObject.CreateInstance<MusicTrack>();
        MusicCue cue = ScriptableObject.CreateInstance<MusicCue>();
        SceneAudioProfile lobby = ScriptableObject.CreateInstance<SceneAudioProfile>();
        SceneAudioProfile fallback = ScriptableObject.CreateInstance<SceneAudioProfile>();
        SceneAudioRegistry registry = ScriptableObject.CreateInstance<SceneAudioRegistry>();

        try
        {
            TestReflection.SetField(track, "trackId", "lobby");
            TestReflection.SetField(cue, "tracks", new[] { track });
            TestReflection.SetField(lobby, "sceneNames", new[] { "Lobby", "Main Menu" });
            TestReflection.SetField(registry, "profiles", new[] { lobby });
            TestReflection.SetField(registry, "fallbackProfile", fallback);

            Assert.That(cue.IsValid, Is.True);
            Assert.That(cue.GetFirstTrack(), Is.SameAs(track));
            Assert.That(lobby.MatchesScene("Lobby"), Is.True);
            Assert.That(lobby.MatchesScene("lobby"), Is.False);
            Assert.That(registry.GetProfileForScene("Main Menu"), Is.SameAs(lobby));
            Assert.That(registry.GetProfileForScene("Game"), Is.SameAs(fallback));
        }
        finally
        {
            Object.DestroyImmediate(track);
            Object.DestroyImmediate(cue);
            Object.DestroyImmediate(lobby);
            Object.DestroyImmediate(fallback);
            Object.DestroyImmediate(registry);
        }
    }

    [Test]
    public void UiSoundTheme_RejectsMissingSoundAndReturnsConfiguredBinding()
    {
        SoundEffect sound = ScriptableObject.CreateInstance<SoundEffect>();
        UiSoundTheme theme = ScriptableObject.CreateInstance<UiSoundTheme>();
        UiSoundBinding binding = new();

        try
        {
            TestReflection.SetField(binding, "type", UiSoundType.Confirm);
            TestReflection.SetField(binding, "sound", sound);
            TestReflection.SetField(theme, "sounds", new[] { binding });

            Assert.That(theme.TryGetSound(UiSoundType.Confirm, out SoundEffect actual), Is.True);
            Assert.That(actual, Is.SameAs(sound));
            Assert.That(theme.TryGetSound(UiSoundType.Error, out _), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(sound);
            Object.DestroyImmediate(theme);
        }
    }

    private static Sprite CreateSprite(Color color)
    {
        Texture2D texture = new(2, 2);
        texture.SetPixels(new[] { color, color, color, color });
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), Vector2.zero);
    }

    private static void DestroySprite(Sprite sprite)
    {
        if (sprite == null)
            return;

        Texture2D texture = sprite.texture;
        Object.DestroyImmediate(sprite);
        Object.DestroyImmediate(texture);
    }
}
