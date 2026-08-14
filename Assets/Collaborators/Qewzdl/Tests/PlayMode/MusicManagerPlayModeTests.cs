using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

[Category("Audio")]
public sealed class MusicManagerPlayModeTests
{
    private const float ClipSeconds = 1f;
    private const int ClipFrequency = 8000;

    private readonly List<Object> cleanup = new();

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        for (int i = cleanup.Count - 1; i >= 0; i--)
        {
            if (cleanup[i] != null)
            {
                Object.DestroyImmediate(cleanup[i]);
            }
        }

        cleanup.Clear();
        yield return null;
    }

    // StopMusic hands its fade-out to a coroutine and returns. Restarting the
    // same cue before that fade lands used to take the "already playing this
    // track" shortcut, which left the fade running; the fade then finished,
    // wiped the cue state the freshly started PlayCueRoutine owns, and the
    // routine dereferenced it on its next loop.
    [UnityTest]
    public IEnumerator RestartingCueDuringFadeOut_KeepsPlayingAndKeepsCueState()
    {
        MusicTrack track = CreateTrack();
        MusicCue cue = CreateCue(track, crossfadeBeforeTrackEnds: 0.2f);
        MusicManager manager = CreateManager();

        manager.PlayCue(cue);
        yield return null;

        Assert.That(
            manager.IsPlaying,
            Is.True,
            "Fixture never started playing, so the shortcut under test is " +
            "unreachable and this test would pass vacuously.");
        Assert.That(manager.CurrentTrack, Is.SameAs(track));

        // Fade shorter than the routine's wait, so it lands while the routine
        // is still suspended - exactly the window that used to break.
        manager.StopMusic(0.3f);
        yield return null;

        manager.PlayCue(cue);
        yield return null;

        yield return new WaitForSecondsRealtime(1.2f);

        Assert.That(
            manager.CurrentCue,
            Is.SameAs(cue),
            "A stale fade-out cleared the cue that had already been restarted.");
        Assert.That(
            manager.IsPlaying,
            Is.True,
            "A stale fade-out silenced the restarted cue.");
    }

    // The plain path still has to work: stopping and leaving it stopped.
    [UnityTest]
    public IEnumerator StopMusic_ClearsCueAndStopsPlayback()
    {
        MusicTrack track = CreateTrack();
        MusicCue cue = CreateCue(track, crossfadeBeforeTrackEnds: 0.2f);
        MusicManager manager = CreateManager();

        manager.PlayCue(cue);
        yield return null;

        manager.StopMusic(0.1f);

        yield return new WaitForSecondsRealtime(0.6f);

        Assert.That(manager.CurrentCue, Is.Null);
        Assert.That(manager.CurrentTrack, Is.Null);
        Assert.That(manager.IsPlaying, Is.False);
    }

    private MusicManager CreateManager()
    {
        GameObject managerObject = Track(new GameObject("Music Manager"));
        return managerObject.AddComponent<MusicManager>();
    }

    private MusicTrack CreateTrack()
    {
        AudioClip clip = Track(
            AudioClip.Create(
                "Test Music Clip",
                (int)(ClipSeconds * ClipFrequency),
                1,
                ClipFrequency,
                false));

        MusicTrack track = Track(ScriptableObject.CreateInstance<MusicTrack>());
        PlayModeTestReflection.SetField(track, "clip", clip);
        PlayModeTestReflection.SetField(track, "volume", 1f);
        PlayModeTestReflection.SetField(track, "fadeInTime", 0f);
        PlayModeTestReflection.SetField(track, "fadeOutTime", 0f);
        return track;
    }

    private MusicCue CreateCue(
        MusicTrack track,
        float crossfadeBeforeTrackEnds)
    {
        MusicCue cue = Track(ScriptableObject.CreateInstance<MusicCue>());
        // No selector, so GetNextTrack falls back to the first track.
        PlayModeTestReflection.SetField(cue, "tracks", new[] { track });
        PlayModeTestReflection.SetField(cue, "loopCue", true);
        PlayModeTestReflection.SetField(cue, "delayBetweenTracks", 0f);
        PlayModeTestReflection.SetField(
            cue,
            "crossfadeBeforeTrackEnds",
            crossfadeBeforeTrackEnds);
        return cue;
    }

    private T Track<T>(T value)
        where T : Object
    {
        cleanup.Add(value);
        return value;
    }
}
