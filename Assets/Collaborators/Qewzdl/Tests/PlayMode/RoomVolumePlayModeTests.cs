using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

[Category("Gameplay")]
public sealed class RoomVolumePlayModeTests
{
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

    // The whole point of allowing several colliders: an L is two boxes, and the
    // notch between their arms has to stay outside the room.
    [UnityTest]
    public IEnumerator LShapedRoom_ContainsBothArmsAndNotTheNotch()
    {
        RoomVolume room = CreateRoom(
            "L Room",
            new Bounds(new Vector3(0f, 1f, 0f), new Vector3(8f, 2f, 2f)),
            new Bounds(new Vector3(-3f, 1f, 3f), new Vector3(2f, 2f, 6f)));

        yield return null;

        Assert.That(
            room.Contains(new Vector3(3f, 1f, 0f)),
            Is.True,
            "Point in the long arm was not inside the room.");
        Assert.That(
            room.Contains(new Vector3(-3f, 1f, 5f)),
            Is.True,
            "Point in the short arm was not inside the room.");
        Assert.That(
            room.Contains(new Vector3(3f, 1f, 5f)),
            Is.False,
            "The notch of the L counted as inside the room.");

        Assert.That(RoomVolume.TryGetRoomAt(
            new Vector3(3f, 1f, 0f),
            out RoomVolume found), Is.True);
        Assert.That(found, Is.SameAs(room));
    }

    // A closet inside a bedroom. Without the smallest-wins rule the answer
    // would depend on which volume registered first.
    [UnityTest]
    public IEnumerator NestedRooms_ResolveToTheSmallestVolume()
    {
        RoomVolume bedroom = CreateRoom(
            "Bedroom",
            new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(10f, 3f, 10f)));
        RoomVolume closet = CreateRoom(
            "Closet",
            new Bounds(new Vector3(4f, 1f, 4f), new Vector3(1.5f, 2f, 1.5f)));

        yield return null;

        Assert.That(
            RoomVolume.TryGetRoomAt(new Vector3(4f, 1f, 4f), out RoomVolume inCloset),
            Is.True);
        Assert.That(
            inCloset,
            Is.SameAs(closet),
            "A point inside both volumes resolved to the larger one.");

        Assert.That(
            RoomVolume.TryGetRoomAt(new Vector3(-3f, 1f, -3f), out RoomVolume inBedroom),
            Is.True);
        Assert.That(inBedroom, Is.SameAs(bedroom));
    }

    // The L shaped nook is genuinely the smaller room - 48 against 75 - but the
    // box drawn around it is 192, so ranking by bounds would hand the overlap
    // to the square room. Exactly the shape the multi part support exists for.
    [UnityTest]
    public IEnumerator OverlappingRooms_RankByShapeNotByTheBoxAroundIt()
    {
        RoomVolume nook = CreateRoom(
            "Nook",
            new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(1f, 3f, 8f)),
            new Bounds(new Vector3(3.5f, 1.5f, 3.5f), new Vector3(8f, 3f, 1f)));
        RoomVolume square = CreateRoom(
            "Square",
            new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(5f, 3f, 5f)));

        yield return null;

        Assert.That(
            nook.Bounds.size.x * nook.Bounds.size.y * nook.Bounds.size.z,
            Is.GreaterThan(square.ShapeVolume),
            "The fixture no longer sets up the case it was written for.");

        Assert.That(
            RoomVolume.TryGetRoomAt(new Vector3(0f, 1.5f, 0f), out RoomVolume inBoth),
            Is.True);
        Assert.That(
            inBoth,
            Is.SameAs(nook),
            "The overlap resolved to the room that is only smaller as a box.");

        Assert.That(
            RoomVolume.TryGetRoomAt(new Vector3(2f, 1.5f, 2f), out RoomVolume inSquare),
            Is.True);
        Assert.That(inSquare, Is.SameAs(square));
    }

    // 6 x 3 x 6 is 108 cubic metres whichever way the room faces, but the box
    // drawn around it at 45 degrees is 216 - enough to lose an overlap to a
    // room that is genuinely bigger.
    [UnityTest]
    public IEnumerator AngledRoom_IsMeasuredByItsShapeNotTheBoxAroundIt()
    {
        RoomVolume angled = CreateRoom(
            "Angled",
            Quaternion.Euler(0f, 45f, 0f),
            new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(6f, 3f, 6f)));
        RoomVolume square = CreateRoom(
            "Square",
            new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(6f, 3f, 7f)));

        yield return null;

        Assert.That(
            angled.ShapeVolume,
            Is.EqualTo(108f).Within(0.5f),
            "A turned room was measured by the box drawn around it.");
        Assert.That(
            square.ShapeVolume,
            Is.GreaterThan(angled.ShapeVolume),
            "The fixture no longer sets up the case it was written for.");

        Assert.That(
            RoomVolume.TryGetRoomAt(new Vector3(0f, 1.5f, 0f), out RoomVolume found),
            Is.True);
        Assert.That(
            found,
            Is.SameAs(angled),
            "The overlap went to the room that is only smaller once " +
            "straightened out.");
    }

    [UnityTest]
    public IEnumerator PointOutsideEveryRoom_ResolvesToNothing()
    {
        CreateRoom(
            "Room",
            new Bounds(new Vector3(0f, 1f, 0f), new Vector3(4f, 2f, 4f)));

        yield return null;

        Assert.That(
            RoomVolume.TryGetRoomAt(new Vector3(50f, 1f, 50f), out RoomVolume found),
            Is.False);
        Assert.That(found, Is.Null);
    }

    [UnityTest]
    public IEnumerator DisabledRoom_LeavesTheRegistry()
    {
        RoomVolume room = CreateRoom(
            "Room",
            new Bounds(new Vector3(0f, 1f, 0f), new Vector3(4f, 2f, 4f)));

        yield return null;

        Assert.That(
            RoomVolume.TryGetRoomAt(Vector3.up, out RoomVolume _),
            Is.True);

        room.gameObject.SetActive(false);
        yield return null;

        Assert.That(
            RoomVolume.TryGetRoomAt(Vector3.up, out RoomVolume _),
            Is.False,
            "A disabled room still answered queries.");
    }

    // A room with no shape answers "no" to everything, which is exactly the
    // kind of failure that should not be silent.
    [UnityTest]
    public IEnumerator RoomWithoutColliders_ReportsItself()
    {
        GameObject roomObject = Track(new GameObject("Empty Room"));
        roomObject.SetActive(false);

        RoomVolume room = roomObject.AddComponent<RoomVolume>();

        LogAssert.Expect(
            LogType.Error,
            new Regex("RoomVolume 'Empty Room' has no colliders"));

        roomObject.SetActive(true);
        yield return null;

        Assert.That(room.HasVolume, Is.False);
        Assert.That(room.Contains(Vector3.zero), Is.False);
    }

    private RoomVolume CreateRoom(string roomName, params Bounds[] parts)
    {
        return CreateRoom(roomName, Quaternion.identity, parts);
    }

    // With a rotation the bounds are read as centre plus local size, since a
    // turned room is exactly the case where the two stop being the same thing.
    private RoomVolume CreateRoom(
        string roomName,
        Quaternion rotation,
        params Bounds[] parts
    )
    {
        GameObject roomObject = Track(new GameObject(roomName));
        roomObject.SetActive(false);
        roomObject.transform.rotation = rotation;

        for (int i = 0; i < parts.Length; i++)
        {
            GameObject part = new($"Part {i}");
            part.transform.SetParent(roomObject.transform, false);
            part.transform.position = parts[i].center;

            BoxCollider partCollider = part.AddComponent<BoxCollider>();
            partCollider.size = parts[i].size;
            partCollider.isTrigger = true;
        }

        RoomVolume room = roomObject.AddComponent<RoomVolume>();
        roomObject.SetActive(true);

        return room;
    }

    private T Track<T>(T value)
        where T : Object
    {
        cleanup.Add(value);
        return value;
    }
}
