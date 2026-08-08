using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

[Category("Gameplay")]
public sealed class PlayerGazePlayModeTests
{
    private readonly List<Object> cleanup = new();

    private NetworkManager manager;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (manager != null && manager.IsListening)
        {
            manager.Shutdown();
        }

        yield return null;

        for (int i = cleanup.Count - 1; i >= 0; i--)
        {
            if (cleanup[i] != null)
            {
                Object.DestroyImmediate(cleanup[i]);
            }
        }

        cleanup.Clear();
        manager = null;

        yield return null;
    }

    [UnityTest]
    public IEnumerator Gaze_SeesWhatIsInFrontAndNotWhatIsBehind()
    {
        yield return StartHost();

        PlayerGazeNetwork gaze = SpawnGaze(Vector3.zero, facingYaw: 0f);

        Assert.That(
            gaze.CanSee(new Vector3(0f, 1.6f, 5f)),
            Is.True,
            "A point straight ahead was not seen.");
        Assert.That(
            gaze.CanSee(new Vector3(0f, 1.6f, -5f)),
            Is.False,
            "A point behind the player was seen.");
        Assert.That(
            gaze.CanSee(new Vector3(0f, 1.6f, 500f)),
            Is.False,
            "A point past the view distance was seen.");
    }

    // The half angle is what makes "behind the player" mean anything, so a
    // point off to the side has to fall outside it.
    [UnityTest]
    public IEnumerator Gaze_DoesNotSeePastItsOwnCone()
    {
        yield return StartHost();

        PlayerGazeNetwork gaze = SpawnGaze(Vector3.zero, facingYaw: 0f);

        Assert.That(
            gaze.CanSee(new Vector3(5f, 1.6f, 5f)),
            Is.True,
            "45 degrees off centre should be inside a 60 degree half angle.");
        Assert.That(
            gaze.CanSee(new Vector3(5f, 1.6f, 1f)),
            Is.False,
            "Nearly side on should be outside a 60 degree half angle.");
    }

    // The point being asked about sits inside the body it belongs to, so a
    // sight line drawn all the way to it hits that body first. Count that as
    // an obstruction and nothing is ever visible - which is exactly what the
    // first build did.
    [UnityTest]
    public IEnumerator Gaze_IsNotBlockedByWhateverItIsLookingAt()
    {
        yield return StartHost();

        PlayerGazeNetwork gaze = SpawnGaze(Vector3.zero, facingYaw: 0f);

        Vector3 target = new(0f, 1.5f, 6f);

        GameObject body = Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
        body.transform.position = target;
        body.layer = LayerMask.NameToLayer("Enemy");
        Physics.SyncTransforms();

        Assert.That(
            body.layer,
            Is.GreaterThanOrEqualTo(0),
            "The project has no Enemy layer, so this fixture proves nothing.");
        Assert.That(
            gaze.CanSee(target),
            Is.True,
            "The enemy's own body counted as something blocking the view of it.");
    }

    // Head over a crate. One probe at chest height calls this hidden, which is
    // the wrong answer to give something deciding whether it has been spotted.
    [UnityTest]
    public IEnumerator Gaze_SeesABodyWhoseHeadClearsCover()
    {
        yield return StartHost();

        PlayerGazeNetwork gaze = SpawnGaze(Vector3.zero, facingYaw: 0f);

        Vector3 feet = new(0f, 0f, 6f);

        // Sized so the sight line to the chest passes under the top and the
        // one to the head passes over it. A shorter crate lets the chest see
        // over as well, and then the test passes with the head probe removed -
        // which is what the first version of it did.
        GameObject crate = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        crate.transform.position = new Vector3(0f, 0.7f, 3f);
        crate.transform.localScale = new Vector3(6f, 1.4f, 0.5f);
        crate.layer = LayerMask.NameToLayer("Walls");
        Physics.SyncTransforms();

        Assert.That(
            gaze.CanSee(feet + Vector3.up * 0.15f),
            Is.False,
            "The fixture no longer hides the knees.");
        Assert.That(
            gaze.CanSee(feet + Vector3.up * (1.8f * 0.55f)),
            Is.False,
            "The fixture no longer hides the chest, so the head probe is not " +
            "what this test is measuring.");
        Assert.That(
            gaze.CanSeeBody(feet, 1.8f),
            Is.True,
            "A body whose head is above the cover was reported hidden.");
    }

    [UnityTest]
    public IEnumerator Gaze_DoesNotSeeABodyFullyBehindCover()
    {
        yield return StartHost();

        PlayerGazeNetwork gaze = SpawnGaze(Vector3.zero, facingYaw: 0f);

        GameObject wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        wall.transform.position = new Vector3(0f, 2f, 3f);
        wall.transform.localScale = new Vector3(6f, 4f, 0.5f);
        wall.layer = LayerMask.NameToLayer("Walls");
        Physics.SyncTransforms();

        Assert.That(
            gaze.CanSeeBody(new Vector3(0f, 0f, 6f), 1.8f),
            Is.False,
            "A body entirely behind a wall was seen.");
    }

    [UnityTest]
    public IEnumerator Gaze_DoesNotSeeThroughAWall()
    {
        yield return StartHost();

        PlayerGazeNetwork gaze = SpawnGaze(Vector3.zero, facingYaw: 0f);

        Vector3 target = new(0f, 1.6f, 6f);

        Assert.That(gaze.CanSee(target), Is.True);

        GameObject wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        wall.transform.position = new Vector3(0f, 1.6f, 3f);
        wall.transform.localScale = new Vector3(6f, 4f, 0.5f);
        Physics.SyncTransforms();

        Assert.That(
            gaze.CanSee(target),
            Is.False,
            "The wall between the eye and the point was seen through.");
    }

    // Turning around is the whole trigger for the ambush, so the answer has to
    // follow the body without anything else being touched.
    [UnityTest]
    public IEnumerator Gaze_FollowsTheBodyTurningRound()
    {
        yield return StartHost();

        PlayerGazeNetwork gaze = SpawnGaze(Vector3.zero, facingYaw: 0f);

        Vector3 behind = new(0f, 1.6f, -5f);

        Assert.That(gaze.CanSee(behind), Is.False);

        gaze.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        Physics.SyncTransforms();

        Assert.That(
            gaze.CanSee(behind),
            Is.True,
            "After turning round, what was behind is in front.");
    }

    private IEnumerator StartHost()
    {
        GameObject root = Track(new GameObject("Gaze host"));
        UnityTransport transport = root.AddComponent<UnityTransport>();
        manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            EnableSceneManagement = false,
            ProtocolVersion = 7
        };
        transport.SetConnectionData("127.0.0.1", 0, "127.0.0.1");

        Assert.That(manager.StartHost(), Is.True);

        float deadline = Time.realtimeSinceStartup + 5f;

        while (!manager.IsHost && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        Assert.That(manager.IsHost, Is.True, "Gaze test host did not start.");
    }

    private PlayerGazeNetwork SpawnGaze(Vector3 position, float facingYaw)
    {
        GameObject root = Track(new GameObject("Gazing player"));
        root.SetActive(false);
        root.transform.SetPositionAndRotation(
            position,
            Quaternion.Euler(0f, facingYaw, 0f));

        NetworkObject networkObject = root.AddComponent<NetworkObject>();
        PlayerGazeNetwork gaze = root.AddComponent<PlayerGazeNetwork>();

        root.SetActive(true);
        networkObject.Spawn();

        return gaze;
    }

    private T Track<T>(T value)
        where T : Object
    {
        cleanup.Add(value);
        return value;
    }
}
