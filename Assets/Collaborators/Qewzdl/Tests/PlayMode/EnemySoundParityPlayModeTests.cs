using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// What one machine hears of the enemy, written down.
internal sealed class RecordedEnemySounds : IGameplaySoundService
{
    internal readonly struct Heard
    {
        internal readonly string Clip;
        internal readonly float Pitch;
        internal readonly double At;

        internal Heard(string clip, float pitch, double at)
        {
            Clip = clip;
            Pitch = pitch;
            At = at;
        }
    }

    private readonly NetworkManager manager;

    internal List<Heard> Sounds { get; } = new();

    internal RecordedEnemySounds(NetworkManager manager)
    {
        this.manager = manager;
    }

    public void Play2D(SoundEffect sound) => Record(sound, SoundRoll.Unshared());
    public void PlayAtPosition(SoundEffect sound, Vector3 position) => Record(sound, SoundRoll.Unshared());
    public void Play2D(SoundEffect sound, SoundRoll roll) => Record(sound, roll);
    public void PlayAtPosition(SoundEffect sound, Vector3 position, SoundRoll roll) => Record(sound, roll);

    public void SetMasterVolume(float volume)
    {
    }

    private void Record(SoundEffect sound, SoundRoll roll)
    {
        Sounds.Add(new Heard(sound.GetClip(roll).name, sound.GetPitch(roll), manager.ServerTime.Time));
    }
}

// The host and a guest hear one enemy alike: the same takes, at the same
// pitch, the same ones left out, at the same moments.
//
// Each machine used to roll its own. Two players in one room heard her
// breathing each on a clock of their own, a different take each time - and
// with a chance below one, one of them heard her where the other heard
// nothing.
[Category("Multiplayer")]
public sealed class EnemySoundParityPlayModeTests : MultiEndpointPlayModeTest
{
    private const uint EnemyPrefabHash = 0x17A60031u;

    private Endpoint host;
    private Endpoint guest;
    private GameObject enemyPrefab;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        yield return StopEndpoints();
        host = null;
        guest = null;
        enemyPrefab = null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator HostAndGuest_HearTheEnemyAlike()
    {
        CreateEnemyPrefab();
        host = CreateEndpoint("Sound host");
        guest = CreateEndpoint("Sound guest");

        foreach (Endpoint endpoint in endpoints)
            endpoint.Manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = enemyPrefab });

        Assert.That(host.Manager.StartHost(), Is.True);
        yield return WaitForCondition(
            () => host.Manager.IsHost && host.Transport.GetLocalEndpoint().Port != 0,
            "The host did not start.");

        guest.Transport.SetConnectionData("127.0.0.1", host.Transport.GetLocalEndpoint().Port);
        Assert.That(guest.Manager.StartClient(), Is.True);
        yield return WaitForCondition(
            () => guest.Manager.IsConnectedClient && host.Manager.ConnectedClientsIds.Count == 2,
            "The guest did not connect.");

        NetworkObject spawned = Track(Object.Instantiate(enemyPrefab)).GetComponent<NetworkObject>();
        PlayModeTestReflection.SetField(spawned, "NetworkManagerOwner", host.Manager);
        spawned.Spawn();
        ulong enemyId = spawned.NetworkObjectId;

        yield return WaitForCondition(
            () => guest.Manager.SpawnManager.SpawnedObjects.ContainsKey(enemyId),
            "The enemy did not reach the guest.");

        RecordedEnemySounds hostHeard = Listen(host, enemyId);
        RecordedEnemySounds guestHeard = Listen(guest, enemyId);
        EnemyNetworkState state = spawned.GetComponent<EnemyNetworkState>();

        // Into a state with sounds on entering and on repeat, and two noises
        // heard - the second inside the reaction's cooldown.
        state.SetStateServer(EnemyState.Patrol);
        yield return new WaitForSecondsRealtime(1f);
        state.ReportHeardNoiseServer(1f, GameplayNoiseSourceType.Item);
        yield return new WaitForSecondsRealtime(0.2f);
        state.ReportHeardNoiseServer(1f, GameplayNoiseSourceType.Item);
        yield return new WaitForSecondsRealtime(2f);

        // Only what both have had time to hear: the guest's clock runs a
        // little behind the host's.
        double heardBy = guest.Manager.ServerTime.Time - 0.5d;
        List<RecordedEnemySounds.Heard> onHost = hostHeard.Sounds.FindAll(s => s.At < heardBy);
        List<RecordedEnemySounds.Heard> onGuest = guestHeard.Sounds.FindAll(s => s.At < heardBy);

        string both = Describe("host", onHost) + Describe("guest", onGuest);

        Assert.That(onHost.Count, Is.GreaterThan(5), "Too little was heard to compare.\n" + both);

        // Agreeing is no use if every roll is the same roll.
        Assert.That(onHost.FindAll(s => s.Clip != onHost[0].Clip), Is.Not.Empty,
            "Every sound was the same take.\n" + both);
        Assert.That(onHost.FindAll(s => Mathf.Abs(s.Pitch - onHost[0].Pitch) > 0.01f), Is.Not.Empty,
            "Every sound was at the same pitch.\n" + both);

        // Each sound either heard, heard by the other as well: the same take at
        // the same pitch, at much the same moment. Not in the same order - two
        // sounds a few hundredths of a second apart on the host can arrive the
        // other way round - and not counted up to the moment the comparison
        // stops: a sound right on it was in on one side and just out on the
        // other. Each is looked for among everything the other heard.
        AssertEachHeardBy(onHost, guestHeard.Sounds, "the guest", both);
        AssertEachHeardBy(onGuest, hostHeard.Sounds, "the host", both);
    }

    private static void AssertEachHeardBy(
        List<RecordedEnemySounds.Heard> sounds,
        List<RecordedEnemySounds.Heard> other,
        string otherName,
        string both)
    {
        List<RecordedEnemySounds.Heard> unmatched = new(other);

        for (int i = 0; i < sounds.Count; i++)
        {
            RecordedEnemySounds.Heard sound = sounds[i];
            int match = unmatched.FindIndex(g =>
                g.Clip == sound.Clip &&
                Mathf.Abs(g.Pitch - sound.Pitch) < 0.0001f &&
                System.Math.Abs(g.At - sound.At) < 0.15d);

            Assert.That(match, Is.Not.EqualTo(-1),
                $"{sound.Clip} x{sound.Pitch:F3} at {sound.At:F3} was not heard alike by {otherName}.\n" + both);
            unmatched.RemoveAt(match);
        }
    }

    private RecordedEnemySounds Listen(Endpoint endpoint, ulong enemyId)
    {
        RecordedEnemySounds heard = new(endpoint.Manager);
        endpoint.Manager.SpawnManager.SpawnedObjects[enemyId]
            .GetComponent<EnemyPresentationController>()
            .Construct(heard);
        return heard;
    }

    private static string Describe(string who, List<RecordedEnemySounds.Heard> sounds)
    {
        StringBuilder text = new($"{who}:\n");

        foreach (RecordedEnemySounds.Heard sound in sounds)
            text.AppendLine($"  {sound.At:F3} {sound.Clip} x{sound.Pitch:F3}");

        return text.ToString();
    }

    // Six takes and a wide pitch, so two machines rolling for themselves
    // could not agree by luck; repeats every few tenths of a second; and a
    // chance below one everywhere it can be set.
    private void CreateEnemyPrefab()
    {
        SoundEffect takes = Track(ScriptableObject.CreateInstance<SoundEffect>());
        AudioClip[] clips = new AudioClip[6];

        for (int i = 0; i < clips.Length; i++)
            clips[i] = Track(AudioClip.Create($"Take {i}", 441, 1, 44100, false));

        PlayModeTestReflection.SetField(takes, "clips", clips);
        PlayModeTestReflection.SetField(takes, "randomizePitch", true);
        PlayModeTestReflection.SetField(takes, "minPitch", 0.5f);
        PlayModeTestReflection.SetField(takes, "maxPitch", 1.5f);

        EnemyLoopingPresentationSound repeat = new();
        PlayModeTestReflection.SetField(repeat, "sound", takes);
        PlayModeTestReflection.SetField(repeat, "minDelay", 0.15f);
        PlayModeTestReflection.SetField(repeat, "maxDelay", 0.35f);
        PlayModeTestReflection.SetField(repeat, "chance", 0.6f);

        EnemyPresentationSound[] onEntering = new EnemyPresentationSound[4];

        for (int i = 0; i < onEntering.Length; i++)
        {
            onEntering[i] = new EnemyPresentationSound();
            PlayModeTestReflection.SetField(onEntering[i], "sound", takes);
            PlayModeTestReflection.SetField(onEntering[i], "delay", 0.1f * i);
            PlayModeTestReflection.SetField(onEntering[i], "chance", 0.5f);
        }

        EnemyStatePresentation patrol = new();
        PlayModeTestReflection.SetField(patrol, "state", EnemyState.Patrol);
        PlayModeTestReflection.SetField(patrol, "enterSounds", onEntering);
        PlayModeTestReflection.SetField(patrol, "loopingSounds", new[] { repeat });
        PlayModeTestReflection.SetField(patrol, "animationSounds", new EnemyAnimationSound[0]);

        EnemyStatePresentation idle = new();
        PlayModeTestReflection.SetField(idle, "state", EnemyState.Idle);
        PlayModeTestReflection.SetField(idle, "enterSounds", new EnemyPresentationSound[0]);
        PlayModeTestReflection.SetField(idle, "loopingSounds", new EnemyLoopingPresentationSound[0]);
        PlayModeTestReflection.SetField(idle, "animationSounds", new EnemyAnimationSound[0]);

        EnemyPresentationSound reaction = new();
        PlayModeTestReflection.SetField(reaction, "sound", takes);
        PlayModeTestReflection.SetField(reaction, "delay", 0.2f);
        PlayModeTestReflection.SetField(reaction, "chance", 1f);

        EnemyPresentationProfile profile = Track(ScriptableObject.CreateInstance<EnemyPresentationProfile>());
        PlayModeTestReflection.SetField(profile, "states", new[] { idle, patrol });
        PlayModeTestReflection.SetField(profile, "useStateIntegerParameter", false);
        PlayModeTestReflection.SetField(profile, "useAttackPhaseIntegerParameter", false);
        PlayModeTestReflection.SetField(profile, "heardLoudNoiseSound", reaction);
        PlayModeTestReflection.SetField(profile, "heardLoudNoiseScore", 0f);
        PlayModeTestReflection.SetField(profile, "heardLoudNoiseCooldown", 0.5f);
        PlayModeTestReflection.SetField(profile, "fallbackAnimationSounds", new EnemyAnimationSound[0]);

        enemyPrefab = Track(new GameObject("Sound parity enemy prefab"));
        enemyPrefab.SetActive(false);
        NetworkObject networkObject = enemyPrefab.AddComponent<NetworkObject>();
        PlayModeTestReflection.SetField(networkObject, "GlobalObjectIdHash", EnemyPrefabHash);
        typeof(NetworkObject)
            .GetProperty(nameof(NetworkObject.IsSceneObject), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SetValue(networkObject, false);
        enemyPrefab.AddComponent<EnemyNetworkState>();
        PlayModeTestReflection.SetField(
            enemyPrefab.AddComponent<EnemyPresentationController>(),
            "profile",
            profile);
        enemyPrefab.SetActive(true);
    }
}
