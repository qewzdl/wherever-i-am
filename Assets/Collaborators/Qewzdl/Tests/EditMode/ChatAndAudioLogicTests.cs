using NUnit.Framework;
using UnityEngine;

[Category("Baseline")]
public sealed class ChatAndAudioLogicTests
{
    [Test]
    public void ChatMessageValidator_NormalizesWhitespaceMarkupAndLineBreaks()
    {
        ChatMessageValidator validator = new();

        bool accepted = validator.TryNormalize(
            "  hi\r\n <b>there</b>  ",
            120,
            out string normalized);

        Assert.That(accepted, Is.True);
        Assert.That(normalized, Is.EqualTo("hi [b]there[/b]"));
    }

    [Test]
    public void ChatMessageValidator_RejectsBlankAndClampsMaximumLength()
    {
        ChatMessageValidator validator = new();

        Assert.That(
            validator.TryNormalize(" \r\n ", 120, out string blank),
            Is.False);
        Assert.That(blank, Is.Empty);

        Assert.That(
            validator.TryNormalize("abcdef", 4, out string truncated),
            Is.True);
        Assert.That(truncated, Is.EqualTo("abcd"));

        Assert.That(
            validator.TryNormalize("xyz", 0, out string clamped),
            Is.True);
        Assert.That(clamped, Is.EqualTo("x"));
    }

    [Test]
    public void ChatMessageData_EqualityIncludesEveryReplicatedField()
    {
        ChatMessageData first = new(
            10,
            20,
            "Player",
            "Message",
            ChatChannel.Game,
            30d);
        ChatMessageData equal = new(
            10,
            20,
            "Player",
            "Message",
            ChatChannel.Game,
            30d);
        ChatMessageData different = new(
            11,
            20,
            "Player",
            "Message",
            ChatChannel.Game,
            30d);

        Assert.That(first.Equals(equal), Is.True);
        Assert.That(first.GetHashCode(), Is.EqualTo(equal.GetHashCode()));
        Assert.That(first.Equals(different), Is.False);
    }

    [Test]
    public void SequentialMusicSelector_CyclesInStableOrder()
    {
        SequentialMusicSelector selector =
            ScriptableObject.CreateInstance<SequentialMusicSelector>();
        MusicTrack first = ScriptableObject.CreateInstance<MusicTrack>();
        MusicTrack second = ScriptableObject.CreateInstance<MusicTrack>();

        try
        {
            MusicTrack[] tracks = { first, second };
            MusicSelectionState state = new();

            Assert.That(selector.SelectNext(tracks, state), Is.SameAs(first));
            Assert.That(selector.SelectNext(tracks, state), Is.SameAs(second));
            Assert.That(selector.SelectNext(tracks, state), Is.SameAs(first));
            Assert.That(state.CurrentIndex, Is.EqualTo(0));
        }
        finally
        {
            Object.DestroyImmediate(selector);
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }
    }

    [Test]
    public void RandomNoRepeatMusicSelector_NeverReturnsLastTrackWhenAlternativeExists()
    {
        RandomNoRepeatMusicSelector selector =
            ScriptableObject.CreateInstance<RandomNoRepeatMusicSelector>();
        MusicTrack first = ScriptableObject.CreateInstance<MusicTrack>();
        MusicTrack second = ScriptableObject.CreateInstance<MusicTrack>();

        try
        {
            MusicTrack[] tracks = { first, second };
            MusicSelectionState state = new() { LastTrack = first };

            for (int i = 0; i < 32; i++)
            {
                Assert.That(
                    selector.SelectNext(tracks, state),
                    Is.SameAs(second));
            }

            Assert.That(selector.SelectNext(null, state), Is.Null);
            Assert.That(selector.SelectNext(new MusicTrack[0], state), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(selector);
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }
    }
}
