using NUnit.Framework;
using UnityEngine;

[Category("Baseline")]
public sealed class RoomVolumeEditorSetupTests
{
    // The two mistakes the button exists to prevent: a solid collider is an
    // invisible wall the size of a room, and a volume on a gameplay layer is
    // picked up by the player's interaction ray.
    [Test]
    public void CreateInScene_ProducesATriggerVolumeOffTheGameplayLayers()
    {
        RoomVolume room = null;

        try
        {
            room = RoomVolumeSetupUtility.CreateInScene();

            Assert.That(room, Is.Not.Null);
            Assert.That(
                RoomVolumeSetupUtility.CountVolumeParts(room),
                Is.EqualTo(1),
                "A new room should arrive with one part ready to size.");

            Collider[] colliders = room.GetComponentsInChildren<Collider>(true);

            Assert.That(colliders, Is.Not.Empty);

            int roomLayer = LayerMask.NameToLayer(
                RoomVolumeSetupUtility.RoomLayerName);

            for (int i = 0; i < colliders.Length; i++)
            {
                Assert.That(
                    colliders[i].isTrigger,
                    Is.True,
                    "A room collider left solid becomes an invisible wall.");
                Assert.That(
                    colliders[i].gameObject.layer,
                    Is.EqualTo(roomLayer));
            }

            Assert.That(
                RoomVolumeSetupUtility.HasCompleteSetup(room),
                Is.True,
                "A freshly created room reported setup problems.");
        }
        finally
        {
            if (room != null)
            {
                Object.DestroyImmediate(room.gameObject);
            }
        }
    }

    [Test]
    public void AddVolumePart_GrowsTheShapeAndLeavesTheListAutomatic()
    {
        RoomVolume room = null;

        try
        {
            room = RoomVolumeSetupUtility.CreateInScene();
            Transform second = RoomVolumeSetupUtility.AddVolumePart(room);

            Assert.That(second, Is.Not.Null);
            Assert.That(
                RoomVolumeSetupUtility.CountVolumeParts(room),
                Is.EqualTo(2));
            Assert.That(
                second.GetComponent<BoxCollider>().isTrigger,
                Is.True);

            // The explicit list stays empty so the room keeps collecting its
            // children - which is the part that actually has to keep working,
            // so assert the room really sees both parts.
            UnityEditor.SerializedObject serialized = new(room);
            UnityEditor.SerializedProperty colliderList = serialized.FindProperty(
                RoomVolumeSetupUtility.VolumeCollidersProperty);

            Assert.That(colliderList, Is.Not.Null);
            Assert.That(
                colliderList.arraySize,
                Is.EqualTo(0),
                "Writing the collected children back would freeze the shape " +
                "and every later part would be ignored.");

            room.Refresh();

            Assert.That(
                room.Colliders.Count,
                Is.EqualTo(2),
                "The room did not pick up the part that was just added.");
        }
        finally
        {
            if (room != null)
            {
                Object.DestroyImmediate(room.gameObject);
            }
        }
    }

    // Sizing a volume to geometry by hand is the slowest part of marking up a
    // level, so the fit has to land on the bounds it was handed.
    [Test]
    public void FitPartToWorldBounds_MatchesTheGivenBounds()
    {
        RoomVolume room = null;

        try
        {
            room = RoomVolumeSetupUtility.CreateInScene();
            Transform part = room.transform.GetChild(0);

            Bounds target = new(
                new Vector3(3f, 1.5f, -2f),
                new Vector3(8f, 3f, 5f));

            RoomVolumeSetupUtility.FitPartToWorldBounds(part, target);

            Bounds fitted = part.GetComponent<BoxCollider>().bounds;

            Assert.That(
                (fitted.center - target.center).magnitude,
                Is.LessThan(0.01f),
                "Fitted part is not centred on the requested bounds.");
            Assert.That(
                (fitted.size - target.size).magnitude,
                Is.LessThan(0.01f),
                "Fitted part does not match the requested size.");
            Assert.That(
                room.Contains(target.center),
                Is.True,
                "The room does not contain the centre of what it was fitted to.");
        }
        finally
        {
            if (room != null)
            {
                Object.DestroyImmediate(room.gameObject);
            }
        }
    }

    // The point of the button: the designer never types a number, the box
    // finds the walls itself.
    [Test]
    public void FitPartsToWalls_GrowsThePartOutToTheWalls()
    {
        GameObject level = null;
        RoomVolume room = null;

        try
        {
            level = BuildBoxRoom();
            room = RoomVolumeSetupUtility.CreateInScene();
            room.transform.position = Vector3.zero;

            Assert.That(
                RoomVolumeSetupUtility.FitPartsToWalls(room),
                Is.EqualTo(1));

            Bounds fitted = room.transform.GetChild(0)
                .GetComponent<BoxCollider>().bounds;

            Assert.That(
                (fitted.size - new Vector3(10f, 3f, 10f)).magnitude,
                Is.LessThan(0.2f),
                $"Fitted {fitted.size} instead of the 10 x 3 x 10 interior.");
            Assert.That(
                (fitted.center - new Vector3(0f, 1.5f, 0f)).magnitude,
                Is.LessThan(0.2f));
        }
        finally
        {
            if (room != null)
            {
                Object.DestroyImmediate(room.gameObject);
            }

            if (level != null)
            {
                Object.DestroyImmediate(level);
            }
        }
    }

    // A single thin probe slips straight through a doorway and keeps going
    // until the next room's far wall. The second pass re-probes with a room
    // wide face, which cannot fit through the gap.
    [Test]
    public void FitPartsToWalls_DoesNotLeakThroughADoorway()
    {
        GameObject level = null;
        RoomVolume room = null;

        try
        {
            level = BuildBoxRoom(doorwayInPositiveX: true);

            // Somewhere for a leaking fit to land, far past the doorway.
            AddBox(level.transform, new Vector3(15.25f, 1.5f, 0f),
                new Vector3(0.5f, 3f, 12f));

            room = RoomVolumeSetupUtility.CreateInScene();
            room.transform.position = Vector3.zero;

            RoomVolumeSetupUtility.FitPartsToWalls(room);

            Bounds fitted = room.transform.GetChild(0)
                .GetComponent<BoxCollider>().bounds;

            Assert.That(
                fitted.max.x,
                Is.LessThan(6f),
                "The volume escaped through the doorway into the next room.");
        }
        finally
        {
            if (room != null)
            {
                Object.DestroyImmediate(room.gameObject);
            }

            if (level != null)
            {
                Object.DestroyImmediate(level);
            }
        }
    }

    // The dropdown reads its current value back out of the same three flags it
    // writes, so a mapping that is not a round trip shows the designer a mode
    // they did not pick.
    [TestCase(RoomHeightMode.FindFloorAndCeiling)]
    [TestCase(RoomHeightMode.FindFloorOnly)]
    [TestCase(RoomHeightMode.FindCeilingOnly)]
    [TestCase(RoomHeightMode.SetByHand)]
    [TestCase(RoomHeightMode.LeaveAsIs)]
    public void HeightMode_RoundTripsThroughTheFlagsItSets(RoomHeightMode mode)
    {
        RoomFitOptions options = RoomFitOptions.Default;
        options.HeightMode = mode;

        Assert.That(options.HeightMode, Is.EqualTo(mode));
    }

    // The mode has to actually drive the fit, not just describe it.
    [Test]
    public void HeightMode_FindFloorOnly_LeavesTheCeilingWhereItWas()
    {
        GameObject level = null;
        RoomVolume room = null;

        try
        {
            level = BuildBoxRoom();
            room = RoomVolumeSetupUtility.CreateInScene();
            room.transform.position = Vector3.zero;

            // Deliberately not the room's own height: a part whose top already
            // sits at the ceiling would pass whether the mode works or not.
            BoxCollider box = room.transform.GetChild(0)
                .GetComponent<BoxCollider>();
            box.center = new Vector3(0f, 1.2f, 0f);
            box.size = new Vector3(6f, 1.6f, 6f);

            RoomFitOptions options = RoomFitOptions.Default;
            options.HeightMode = RoomHeightMode.FindFloorOnly;

            RoomVolumeSetupUtility.FitPartsToWalls(room, options);

            Bounds fitted = box.bounds;

            Assert.That(
                fitted.min.y,
                Is.EqualTo(0f).Within(0.05f),
                "The floor was not found.");
            Assert.That(
                fitted.max.y,
                Is.EqualTo(2f).Within(0.05f),
                "The ceiling moved to the real ceiling at 3 even though the " +
                "mode keeps it.");
        }
        finally
        {
            if (room != null)
            {
                Object.DestroyImmediate(room.gameObject);
            }

            if (level != null)
            {
                Object.DestroyImmediate(level);
            }
        }
    }

    // A wardrobe against the wall used to end the room at the wardrobe. Only
    // the wall layers stop a probe now, so the furniture is seen through.
    [Test]
    public void FitPartsToWalls_SeesThroughFurnitureInsideTheRoom()
    {
        GameObject level = null;
        RoomVolume room = null;

        try
        {
            level = BuildBoxRoom();

            // A wardrobe standing off the +X wall, squarely in the probe's way.
            AddBox(level.transform, new Vector3(3f, 1.5f, 0f),
                new Vector3(2f, 3f, 2f), "Interactable");

            room = RoomVolumeSetupUtility.CreateInScene();
            room.transform.position = Vector3.zero;

            RoomVolumeSetupUtility.FitPartsToWalls(room);

            Bounds fitted = room.transform.GetChild(0)
                .GetComponent<BoxCollider>().bounds;

            Assert.That(
                fitted.max.x,
                Is.EqualTo(5f).Within(0.2f),
                "The room stopped at the furniture instead of the wall.");
            Assert.That(fitted.size.x, Is.EqualTo(10f).Within(0.2f));
        }
        finally
        {
            if (room != null)
            {
                Object.DestroyImmediate(room.gameObject);
            }

            if (level != null)
            {
                Object.DestroyImmediate(level);
            }
        }
    }

    // A room built at an angle is the case a world axis fit cannot do better
    // than the box drawn around it. Rotating the part is the whole answer,
    // because the probes travel along the part's own axes.
    [Test]
    public void FitPartsToWalls_FollowsARoomBuiltAtAnAngle()
    {
        GameObject level = null;
        RoomVolume room = null;
        Quaternion angle = Quaternion.Euler(0f, 30f, 0f);

        try
        {
            level = BuildBoxRoom();
            level.transform.rotation = angle;

            room = RoomVolumeSetupUtility.CreateInScene();
            room.transform.SetPositionAndRotation(Vector3.zero, angle);

            Assert.That(
                RoomVolumeSetupUtility.FitPartsToWalls(room),
                Is.EqualTo(1));

            Transform part = room.transform.GetChild(0);
            BoxCollider box = part.GetComponent<BoxCollider>();

            Assert.That(
                (box.size - new Vector3(10f, 3f, 10f)).magnitude,
                Is.LessThan(0.3f),
                $"Fitted {box.size} instead of the 10 x 3 x 10 interior.");
            Assert.That(
                Quaternion.Angle(part.rotation, angle),
                Is.LessThan(0.5f),
                "The fit straightened the part and lost the room's angle.");
        }
        finally
        {
            if (room != null)
            {
                Object.DestroyImmediate(room.gameObject);
            }

            if (level != null)
            {
                Object.DestroyImmediate(level);
            }
        }
    }

    // The part is dropped in straight and the room is not. The angle is read
    // off the wall normals, so the designer no longer has to match it by hand
    // and then find out it was two degrees out.
    [Test]
    public void FitPartsToWalls_TakesTheRoomsAngleFromItsWalls()
    {
        GameObject level = null;
        RoomVolume room = null;

        try
        {
            // Not square: a 10 by 10 room would fit at any quarter turn and
            // the test could not tell a found angle from a lucky one.
            level = BuildBoxRoom(halfX: 5f, halfZ: 3f);
            level.transform.rotation = Quaternion.Euler(0f, 30f, 0f);

            room = RoomVolumeSetupUtility.CreateInScene();
            room.transform.SetPositionAndRotation(
                Vector3.zero, Quaternion.identity);

            Assert.That(
                RoomVolumeSetupUtility.FitPartsToWalls(room),
                Is.EqualTo(1));

            Transform part = room.transform.GetChild(0);

            // A rectangle repeats every quarter turn, so that is all the angle
            // can ever be known to.
            Assert.That(
                Mathf.Repeat(part.eulerAngles.y, 90f),
                Is.EqualTo(30f).Within(1f),
                $"Settled at {part.eulerAngles.y:0.##} degrees.");

            Vector3 size = part.GetComponent<BoxCollider>().size;

            Assert.That(
                (size - new Vector3(10f, 3f, 6f)).magnitude,
                Is.LessThan(0.3f),
                $"Fitted {size} instead of the 10 x 3 x 6 interior.");
        }
        finally
        {
            if (room != null)
            {
                Object.DestroyImmediate(room.gameObject);
            }

            if (level != null)
            {
                Object.DestroyImmediate(level);
            }
        }
    }

    // The walls pin the angle down to one of four quarter turns, never to one.
    // Landing on a different one than the designer chose spins the box and
    // moves every side onto a different wall, which reads as a wrong answer
    // even though it is the same angle.
    [Test]
    public void FitPartsToWalls_KeepsTheQuarterTurnTheDesignerChose()
    {
        GameObject level = null;
        RoomVolume room = null;

        try
        {
            level = BuildBoxRoom(halfX: 5f, halfZ: 3f);
            level.transform.rotation = Quaternion.Euler(0f, 30f, 0f);

            room = RoomVolumeSetupUtility.CreateInScene();
            room.transform.SetPositionAndRotation(
                Vector3.zero, Quaternion.Euler(0f, 120f, 0f));

            RoomVolumeSetupUtility.FitPartsToWalls(room);

            Transform part = room.transform.GetChild(0);

            Assert.That(
                Mathf.Abs(Mathf.DeltaAngle(part.eulerAngles.y, 120f)),
                Is.LessThan(1f),
                $"Snapped from 120 to {part.eulerAngles.y:0.##} degrees.");
        }
        finally
        {
            if (room != null)
            {
                Object.DestroyImmediate(room.gameObject);
            }

            if (level != null)
            {
                Object.DestroyImmediate(level);
            }
        }
    }

    // Rays that leave through an opening report the angle of whatever they
    // land in. An equal vote for each ray lets the room next door decide how
    // this one is turned.
    [Test]
    public void FitPartsToWalls_IsNotTurnedByWhatItSeesThroughAnOpening()
    {
        GameObject level = null;
        GameObject neighbour = null;
        RoomVolume room = null;

        try
        {
            // Short and wide, so the missing wall takes a large slice of the
            // circle the probes sweep. A deeper room hides the problem: one
            // stray ray out of sixteen cannot shift the average enough to see.
            level = BuildBoxRoom(halfX: 2f, halfZ: 3f, openInPositiveX: true);
            level.transform.rotation = Quaternion.Euler(0f, 30f, 0f);

            // Straight on the world axes, well beyond the opening, so every
            // ray that escapes votes for a different angle than this room.
            neighbour = new GameObject("Neighbour");
            AddBox(neighbour.transform, new Vector3(18f, 1.5f, 0f),
                new Vector3(0.5f, 6f, 40f));
            AddBox(neighbour.transform, new Vector3(9f, 1.5f, 14f),
                new Vector3(40f, 6f, 0.5f));
            AddBox(neighbour.transform, new Vector3(9f, 1.5f, -14f),
                new Vector3(40f, 6f, 0.5f));
            Physics.SyncTransforms();

            room = RoomVolumeSetupUtility.CreateInScene();
            room.transform.SetPositionAndRotation(
                Vector3.zero, Quaternion.identity);

            RoomVolumeSetupUtility.FitPartsToWalls(room);

            Transform part = room.transform.GetChild(0);

            Assert.That(
                Mathf.Repeat(part.eulerAngles.y, 90f),
                Is.EqualTo(30f).Within(2f),
                $"The room next door pulled the angle to " +
                $"{Mathf.Repeat(part.eulerAngles.y, 90f):0.##} degrees.");
        }
        finally
        {
            if (room != null)
            {
                Object.DestroyImmediate(room.gameObject);
            }

            if (neighbour != null)
            {
                Object.DestroyImmediate(neighbour);
            }

            if (level != null)
            {
                Object.DestroyImmediate(level);
            }
        }
    }

    // The failure an average cannot avoid: something inside the room that is
    // not square to it. Fifteen degrees off is the worst kind - far enough to
    // drag the answer, near enough that it does not cancel itself out.
    [Test]
    public void FitPartsToWalls_IgnoresGeometryThatIsNotSquareToTheRoom()
    {
        GameObject level = null;
        RoomVolume room = null;

        try
        {
            level = BuildBoxRoom(halfX: 5f, halfZ: 3f);
            level.transform.rotation = Quaternion.Euler(0f, 30f, 0f);

            // A crate stood askew. It has to take a real share of the probes
            // but stay a minority - crowd the probe origin and there is
            // genuinely no telling which angle is the room's, which the
            // algorithm is entitled to refuse. 22.5 degrees off the room is
            // the worst case: a quarter turn away once the angles are folded,
            // so every one of its votes pulls as hard as a vote can.
            GameObject crate = new("Walls");
            crate.transform.SetParent(level.transform, false);
            crate.transform.localPosition = new Vector3(3f, 1.5f, 0f);
            crate.transform.localRotation = Quaternion.Euler(0f, 22.5f, 0f);
            crate.layer = LayerMask.NameToLayer(
                RoomVolumeSetupUtility.WallLayerName);
            crate.AddComponent<BoxCollider>().size = new Vector3(3f, 3f, 3f);
            Physics.SyncTransforms();

            room = RoomVolumeSetupUtility.CreateInScene();
            room.transform.SetPositionAndRotation(
                Vector3.zero, Quaternion.identity);

            RoomVolumeSetupUtility.FitPartsToWalls(room);

            Transform part = room.transform.GetChild(0);

            Assert.That(
                Mathf.Repeat(part.eulerAngles.y, 90f),
                Is.EqualTo(30f).Within(1.5f),
                "The crate pulled the room's angle to " +
                $"{Mathf.Repeat(part.eulerAngles.y, 90f):0.##} degrees.");
        }
        finally
        {
            if (room != null)
            {
                Object.DestroyImmediate(room.gameObject);
            }

            if (level != null)
            {
                Object.DestroyImmediate(level);
            }
        }
    }

    [Test]
    public void FitPartsToWalls_LeavesAStraightRoomStraight()
    {
        GameObject level = null;
        RoomVolume room = null;

        try
        {
            level = BuildBoxRoom();
            room = RoomVolumeSetupUtility.CreateInScene();
            room.transform.position = Vector3.zero;

            RoomVolumeSetupUtility.FitPartsToWalls(room);

            Transform part = room.transform.GetChild(0);

            Assert.That(
                Mathf.Abs(Mathf.DeltaAngle(
                    Mathf.Repeat(part.eulerAngles.y, 90f), 0f)),
                Is.LessThan(1f),
                $"A square room turned the part to {part.eulerAngles.y:0.##}.");
        }
        finally
        {
            if (room != null)
            {
                Object.DestroyImmediate(room.gameObject);
            }

            if (level != null)
            {
                Object.DestroyImmediate(level);
            }
        }
    }

    // A part placed by hand, in a spot no probe would get right, has to
    // survive the button. Locking is per part, unlike the side switches which
    // apply to the whole room.
    [Test]
    public void FitPartsToWalls_LeavesLockedPartsAlone()
    {
        GameObject level = null;
        RoomVolume room = null;

        try
        {
            level = BuildBoxRoom(halfX: 5f, halfZ: 3f);

            room = RoomVolumeSetupUtility.CreateInScene();
            room.transform.position = Vector3.zero;
            RoomVolumeSetupUtility.AddVolumePart(room);

            BoxCollider locked = room.transform.GetChild(0)
                .GetComponent<BoxCollider>();
            BoxCollider free = room.transform.GetChild(1)
                .GetComponent<BoxCollider>();

            Vector3 lockedSizeBefore = locked.size;
            Vector3 lockedCentreBefore = locked.center;

            RoomVolumeSetupUtility.SetPartLocked(room, locked, true);

            Assert.That(
                room.IsPartLocked(locked),
                Is.True,
                "The lock did not take.");
            Assert.That(room.IsPartLocked(free), Is.False);
            Assert.That(
                RoomVolumeSetupUtility.CountUnlockedParts(room),
                Is.EqualTo(1));

            Assert.That(
                RoomVolumeSetupUtility.FitPartsToWalls(room),
                Is.EqualTo(1),
                "The locked part was fitted as well.");

            Assert.That(
                locked.size,
                Is.EqualTo(lockedSizeBefore),
                $"The locked part was resized to {locked.size}.");
            Assert.That(locked.center, Is.EqualTo(lockedCentreBefore));

            Assert.That(
                (free.size - new Vector3(10f, 3f, 6f)).magnitude,
                Is.LessThan(0.3f),
                $"The unlocked part fitted to {free.size} instead.");
        }
        finally
        {
            if (room != null)
            {
                Object.DestroyImmediate(room.gameObject);
            }

            if (level != null)
            {
                Object.DestroyImmediate(level);
            }
        }
    }

    [Test]
    public void SetPartLocked_UnlocksAgain()
    {
        RoomVolume room = null;

        try
        {
            room = RoomVolumeSetupUtility.CreateInScene();

            Collider part = room.transform.GetChild(0).GetComponent<Collider>();

            RoomVolumeSetupUtility.SetPartLocked(room, part, true);
            RoomVolumeSetupUtility.SetPartLocked(room, part, false);

            Assert.That(
                room.IsPartLocked(part),
                Is.False,
                "Clearing an object reference array element leaves a hole " +
                "unless it is nulled before it is deleted.");
        }
        finally
        {
            if (room != null)
            {
                Object.DestroyImmediate(room.gameObject);
            }
        }
    }

    // The escape hatch for rooms the probe reads badly - an open side, a wall
    // of glass, a courtyard. Switch that side off and it keeps what it had.
    [Test]
    public void FitPartsToWalls_LeavesSwitchedOffSidesAlone()
    {
        GameObject level = null;
        RoomVolume room = null;

        try
        {
            level = BuildBoxRoom();
            room = RoomVolumeSetupUtility.CreateInScene();
            room.transform.position = Vector3.zero;

            RoomFitOptions options = RoomFitOptions.Default;
            options.fitMaxX = false;

            RoomVolumeSetupUtility.FitPartsToWalls(room, options);

            Bounds fitted = room.transform.GetChild(0)
                .GetComponent<BoxCollider>().bounds;

            // -X finds the wall at 5, +X keeps the default half extent of 3.
            Assert.That(
                fitted.size.x,
                Is.EqualTo(8f).Within(0.2f),
                $"Fitted {fitted.size.x} across X instead of 5 + 3.");
            Assert.That(fitted.max.x, Is.EqualTo(3f).Within(0.2f));
            Assert.That(fitted.size.z, Is.EqualTo(10f).Within(0.2f));
        }
        finally
        {
            if (room != null)
            {
                Object.DestroyImmediate(room.gameObject);
            }

            if (level != null)
            {
                Object.DestroyImmediate(level);
            }
        }
    }

    [Test]
    public void FitPartsToWalls_UsesTheHeightItIsGiven()
    {
        GameObject level = null;
        RoomVolume room = null;

        try
        {
            level = BuildBoxRoom();
            room = RoomVolumeSetupUtility.CreateInScene();
            room.transform.position = Vector3.zero;

            RoomFitOptions options = RoomFitOptions.Default;
            options.useExplicitHeight = true;
            options.floorY = 0.5f;
            options.ceilingY = 2f;

            RoomVolumeSetupUtility.FitPartsToWalls(room, options);

            Bounds fitted = room.transform.GetChild(0)
                .GetComponent<BoxCollider>().bounds;

            Assert.That(
                fitted.min.y,
                Is.EqualTo(0.5f).Within(0.05f),
                "The floor was found by probing instead of being taken as given.");
            Assert.That(fitted.max.y, Is.EqualTo(2f).Within(0.05f));
            Assert.That(fitted.size.x, Is.EqualTo(10f).Within(0.2f));
        }
        finally
        {
            if (room != null)
            {
                Object.DestroyImmediate(room.gameObject);
            }

            if (level != null)
            {
                Object.DestroyImmediate(level);
            }
        }
    }

    // The workflow this exists for: a room that is two corridors meeting at a
    // corner, marked up by clicking once in each rather than dragging boxes
    // out of the room's origin.
    [Test]
    public void AddVolumePartAt_MarksUpARoomOfPassagesFromTwoClicks()
    {
        GameObject level = null;
        RoomVolume room = null;

        try
        {
            level = BuildCorridorRoom();
            room = RoomVolumeSetupUtility.CreateInScene();
            room.transform.position = Vector3.zero;

            // The part CreateInScene comes with is not in either corridor.
            // Refreshing here would report a room with no shape, which is true
            // for the one line it lasts and not what the test is about.
            Object.DestroyImmediate(room.transform.GetChild(0).gameObject);

            RoomFitOptions options = RoomFitOptions.Default;

            Assert.That(
                RoomVolumeSetupUtility.AddVolumePartAt(
                    room, new Vector3(0f, 0f, 0f), options),
                Is.Not.Null);
            Assert.That(
                RoomVolumeSetupUtility.AddVolumePartAt(
                    room, new Vector3(5f, 0f, 5f), options),
                Is.Not.Null);

            Assert.That(
                room.Contains(new Vector3(-5f, 1f, 0f)),
                Is.True,
                "The far end of the long corridor is not in the room.");
            Assert.That(
                room.Contains(new Vector3(5f, 1f, 7f)),
                Is.True,
                "The far end of the side corridor is not in the room.");
            Assert.That(
                room.Contains(new Vector3(-3f, 1f, 5f)),
                Is.False,
                "The space between the two corridors counted as inside.");
        }
        finally
        {
            if (room != null)
            {
                Object.DestroyImmediate(room.gameObject);
            }

            if (level != null)
            {
                Object.DestroyImmediate(level);
            }
        }
    }

    // An L of two corridors: a long one along X at z in [-1, 1], and a side
    // one along Z at x in [4, 6]. Both 3 metres tall.
    private static GameObject BuildCorridorRoom()
    {
        GameObject level = new("Corridors");

        AddBox(level.transform, new Vector3(0f, -0.25f, 0f),
            new Vector3(20f, 0.5f, 20f));
        AddBox(level.transform, new Vector3(0f, 3.25f, 0f),
            new Vector3(20f, 0.5f, 20f));

        // Long corridor: both long walls, with the side corridor's mouth left
        // open in the north one.
        AddBox(level.transform, new Vector3(0f, 1.5f, -1.25f),
            new Vector3(13f, 3f, 0.5f));
        AddBox(level.transform, new Vector3(-1.25f, 1.5f, 1.25f),
            new Vector3(10.5f, 3f, 0.5f));
        AddBox(level.transform, new Vector3(-6.25f, 1.5f, 0f),
            new Vector3(0.5f, 3f, 2.5f));

        // Side corridor.
        AddBox(level.transform, new Vector3(3.75f, 1.5f, 4.75f),
            new Vector3(0.5f, 3f, 7.5f));
        AddBox(level.transform, new Vector3(6.25f, 1.5f, 3.5f),
            new Vector3(0.5f, 3f, 9.5f));
        AddBox(level.transform, new Vector3(5f, 1.5f, 8.25f),
            new Vector3(2.5f, 3f, 0.5f));

        Physics.SyncTransforms();

        return level;
    }

    // Interior 2*halfX by 3 by 2*halfZ centred on the origin, walls half a
    // metre thick.
    private static GameObject BuildBoxRoom(
        bool doorwayInPositiveX = false,
        float halfX = 5f,
        float halfZ = 5f,
        bool openInPositiveX = false
    )
    {
        GameObject level = new("Level");

        float spanX = halfX * 2f + 2f;
        float spanZ = halfZ * 2f + 2f;

        AddBox(level.transform, new Vector3(0f, -0.25f, 0f),
            new Vector3(spanX, 0.5f, spanZ));
        AddBox(level.transform, new Vector3(0f, 3.25f, 0f),
            new Vector3(spanX, 0.5f, spanZ));
        AddBox(level.transform, new Vector3(-(halfX + 0.25f), 1.5f, 0f),
            new Vector3(0.5f, 3f, spanZ));
        AddBox(level.transform, new Vector3(0f, 1.5f, halfZ + 0.25f),
            new Vector3(spanX, 3f, 0.5f));
        AddBox(level.transform, new Vector3(0f, 1.5f, -(halfZ + 0.25f)),
            new Vector3(spanX, 3f, 0.5f));

        if (openInPositiveX)
        {
            // No wall at all on that side.
        }
        else if (doorwayInPositiveX)
        {
            AddBox(level.transform, new Vector3(halfX + 0.25f, 1.5f, 3.3f),
                new Vector3(0.5f, 3f, 5.4f));
            AddBox(level.transform, new Vector3(halfX + 0.25f, 1.5f, -3.3f),
                new Vector3(0.5f, 3f, 5.4f));
        }
        else
        {
            AddBox(level.transform, new Vector3(halfX + 0.25f, 1.5f, 0f),
                new Vector3(0.5f, 3f, spanZ));
        }

        Physics.SyncTransforms();

        return level;
    }

    private static void AddBox(
        Transform parent,
        Vector3 centre,
        Vector3 size,
        string layerName = RoomVolumeSetupUtility.WallLayerName
    )
    {
        GameObject box = new(layerName);
        box.transform.SetParent(parent, false);
        box.transform.position = centre;
        box.AddComponent<BoxCollider>().size = size;

        int layer = LayerMask.NameToLayer(layerName);

        Assert.That(
            layer,
            Is.GreaterThanOrEqualTo(0),
            $"The project has no '{layerName}' layer, so this fixture cannot " +
            "set up the case it was written for.");

        box.layer = layer;
    }

    // The mis-authoring that hides itself: the volume looks right, the gizmo
    // draws, the inspector says ready, and every lookup at floor level
    // answers "no room".
    [Test]
    public void GetSetupProblems_ReportsAVolumeThatStartsAboveTheFloor()
    {
        GameObject level = null;
        RoomVolume room = null;

        try
        {
            level = new GameObject("Level");
            AddBox(level.transform, new Vector3(0f, -0.25f, 0f),
                new Vector3(20f, 0.5f, 20f));
            Physics.SyncTransforms();

            room = RoomVolumeSetupUtility.CreateInScene();
            room.transform.position = Vector3.zero;

            BoxCollider box = room.transform.GetChild(0)
                .GetComponent<BoxCollider>();
            box.center = new Vector3(0f, 1.8f, 0f);
            box.size = new Vector3(6f, 3f, 6f);
            Physics.SyncTransforms();

            float topBefore = box.bounds.max.y;

            Assert.That(
                RoomVolumeSetupUtility.TryMeasureFloorGap(box, out float gap),
                Is.True);
            Assert.That(gap, Is.EqualTo(0.3f).Within(0.02f));
            Assert.That(
                RoomVolumeSetupUtility.HasCompleteSetup(room),
                Is.False,
                "A room floating above its floor was reported as ready.");

            Assert.That(
                RoomVolumeSetupUtility.DropPartsToFloor(room),
                Is.EqualTo(1));
            Physics.SyncTransforms();

            Assert.That(
                box.bounds.min.y,
                Is.EqualTo(0f).Within(0.06f),
                "The part was not brought down onto the floor.");
            Assert.That(
                box.bounds.max.y,
                Is.EqualTo(topBefore).Within(0.02f),
                "Dropping the part moved its ceiling as well.");
            Assert.That(
                RoomVolumeSetupUtility.HasCompleteSetup(room),
                Is.True,
                string.Join("; ", RoomVolumeSetupUtility.GetSetupProblems(room)));
        }
        finally
        {
            if (room != null)
            {
                Object.DestroyImmediate(room.gameObject);
            }

            if (level != null)
            {
                Object.DestroyImmediate(level);
            }
        }
    }

    [Test]
    public void FixCollidersAndLayer_RepairsHandBuiltVolumes()
    {
        GameObject root = null;

        try
        {
            root = new GameObject("Hand Built Room");
            root.layer = 0;

            GameObject part = new("Part");
            part.transform.SetParent(root.transform, false);
            part.layer = 0;
            part.AddComponent<BoxCollider>().isTrigger = false;

            RoomVolume room = root.AddComponent<RoomVolume>();

            Assert.That(
                RoomVolumeSetupUtility.HasCompleteSetup(room),
                Is.False,
                "A solid collider on a gameplay layer should be reported.");

            RoomVolumeSetupUtility.FixCollidersAndLayer(room);

            Assert.That(
                RoomVolumeSetupUtility.HasCompleteSetup(room),
                Is.True,
                RoomVolumeSetupUtility.GetSetupProblems(room).Count > 0
                    ? string.Join("; ",
                        RoomVolumeSetupUtility.GetSetupProblems(room))
                    : string.Empty);
        }
        finally
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
