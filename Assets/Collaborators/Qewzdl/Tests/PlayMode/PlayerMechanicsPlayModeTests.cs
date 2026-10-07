using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;

[Category("Gameplay")]
public sealed class PlayerMechanicsPlayModeTests
{
    private readonly List<Object> cleanup = new();

    [TearDown]
    public void TearDown()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        for (int i = cleanup.Count - 1; i >= 0; i--)
        {
            if (cleanup[i] != null)
                Object.DestroyImmediate(cleanup[i]);
        }

        cleanup.Clear();
    }

    // The cursor is one global thing. Another player joining sets their camera
    // up here with local control off, and that released the cursor the local
    // player was holding - after which looking around stopped dead, because it
    // needs the cursor locked. Opening the pause menu and closing it locked
    // the cursor again, which is why that appeared to fix joining a server.
    [Test]
    public void RemotePlayerCamera_DoesNotReleaseTheLocalCursor()
    {
        CameraLook remote = CreateLockingCameraLook("Remote player");

        // Batch mode has no window, so the lock state itself does not take.
        // Visibility is the other half of the same call and is observable, so
        // that is what the released cursor would show up in.
        Cursor.visible = false;

        Assert.That(
            Cursor.visible,
            Is.False,
            "Cursor visibility is not observable here, so this fixture " +
            "cannot tell whether anything released the cursor.");

        remote.SetLocalControl(false);

        Assert.That(
            Cursor.visible,
            Is.False,
            "Another player's camera released the local player's cursor.");
    }

    private CameraLook CreateLockingCameraLook(string name)
    {
        GameObject player = Track(new GameObject(name));
        player.SetActive(false);
        player.AddComponent<Rigidbody>().useGravity = false;

        GameObject cameraObject = Track(new GameObject($"{name} camera"));
        cameraObject.SetActive(false);
        cameraObject.transform.SetParent(player.transform, false);

        CameraLook cameraLook = cameraObject.AddComponent<CameraLook>();
        PlayModeTestReflection.SetField(
            cameraLook,
            "playerTransform",
            player.transform);

        player.SetActive(true);
        cameraObject.SetActive(true);
        return cameraLook;
    }

    [Test]
    public void InputHandler_RequiresEveryBlockerToReleaseInput()
    {
        GameObject player = Track(new GameObject("Input player"));
        player.SetActive(false);
        player.AddComponent<Rigidbody>().useGravity = false;

        GameObject cameraObject = Track(new GameObject("Look camera"));
        cameraObject.SetActive(false);
        cameraObject.transform.SetParent(player.transform, false);
        CameraLook cameraLook = cameraObject.AddComponent<CameraLook>();
        PlayModeTestReflection.SetField(cameraLook, "playerTransform", player.transform);
        PlayModeTestReflection.SetField(cameraLook, "lockCursorOnLocalControl", false);
        PlayModeTestReflection.SetField(cameraLook, "unlockCursorWhenLookBlocked", false);
        PlayModeTestReflection.SetField(cameraLook, "lockCursorWhenLookUnblocked", false);

        // A player that cannot read crouch is a broken player, and the handler
        // says so out loud - deliberately, because the version before it wired
        // crouch through a serialised list and failed silently in a build that
        // compiled. This fixture had no PlayerInput at all, so since crouch
        // started being read here the handler had been logging that error on
        // every run and the test had been red on it, while testing something
        // else entirely: which blockers release the input.
        //
        // So the player gets the one action it is missing. Nothing here
        // presses it; it is here so the thing under test is a player rather
        // than a player with a hole in it.
        InputActionAsset actions = Track(ScriptableObject.CreateInstance<InputActionAsset>());
        actions.name = "Input handler test actions";
        InputActionMap playerMap = actions.AddActionMap("Player");
        playerMap.AddAction("Crouch", InputActionType.Button);
        playerMap.AddAction("Run", InputActionType.Button);

        PlayerInput playerInput = player.AddComponent<PlayerInput>();
        playerInput.actions = actions;

        PlayerInputHandler input = player.AddComponent<PlayerInputHandler>();
        PlayModeTestReflection.SetField(input, "cameraLook", cameraLook);
        PlayerOrchestrator orchestrator = player.AddComponent<PlayerOrchestrator>();
        cameraObject.SetActive(true);
        player.SetActive(true);
        orchestrator.Setup(isMultiplayer: false, isOwner: true);

        object menu = new();
        object cutscene = new();

        input.SetInputActive(menu, false);
        Assert.That(
            PlayModeTestReflection.GetField<bool>(input, "inputActive"),
            Is.False);
        Assert.That(
            PlayModeTestReflection.GetField<bool>(cameraLook, "lookActive"),
            Is.False);

        input.SetInputActive(cutscene, false);
        input.SetInputActive(menu, true);
        Assert.That(
            PlayModeTestReflection.GetField<bool>(input, "inputActive"),
            Is.False);

        input.SetInputActive(cutscene, true);
        Assert.That(
            PlayModeTestReflection.GetField<bool>(input, "inputActive"),
            Is.True);
        Assert.That(
            PlayModeTestReflection.GetField<bool>(cameraLook, "lookActive"),
            Is.True);
    }

    [Test]
    public void PlayerActionGate_ArbitratesConflictingActionsAtomically()
    {
        GameObject player = Track(new GameObject("Action gate player"));
        PlayerActionGate gate = player.AddComponent<PlayerActionGate>();
        object pickup = new();
        object drag = new();
        object hiding = new();

        Assert.That(
            gate.TryBegin(PlayerActionKind.Pickup, pickup),
            Is.True);
        Assert.That(gate.ActiveAction, Is.EqualTo(PlayerActionKind.Pickup));
        Assert.That(
            gate.TryBegin(PlayerActionKind.Drag, drag),
            Is.False);

        // Hiding is the one action a carry makes room for: a player holding a
        // torch can still climb into a cupboard, and the carry waits there for
        // them. Everything else in this test is the exclusivity that stands.
        Assert.That(
            gate.TryBegin(PlayerActionKind.Hiding, hiding),
            Is.True);
        Assert.That(gate.ActiveAction, Is.EqualTo(PlayerActionKind.Hiding));
        Assert.That(
            gate.End(PlayerActionKind.Hiding, hiding),
            Is.True);
        Assert.That(gate.ActiveAction, Is.EqualTo(PlayerActionKind.Pickup));

        Assert.That(
            gate.End(PlayerActionKind.Pickup, drag),
            Is.False,
            "A different mechanic must not release the active action.");

        gate.Confirm(PlayerActionKind.Hiding, hiding);

        Assert.That(gate.ActiveAction, Is.EqualTo(PlayerActionKind.Hiding));

        // The assertion this replaces read the return value; what it was
        // guarding is the state. A late pickup response is now answered by
        // forgetting the carry that was waiting behind the hiding, and the
        // hiding itself is what must survive it.
        gate.End(PlayerActionKind.Pickup, pickup);
        Assert.That(
            gate.ActiveAction,
            Is.EqualTo(PlayerActionKind.Hiding),
            "A late pickup response must not clear authoritative hiding.");

        Assert.That(
            gate.End(PlayerActionKind.Hiding, hiding),
            Is.True);
        Assert.That(gate.IsBusy, Is.False);
    }

    [Test]
    public void HidingEffects_TouchOnlyExplicitVisualsAndColliders()
    {
        GameObject player = Track(new GameObject("Explicit hiding player"));
        Rigidbody body = player.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeRotation;

        Transform visualRoot = CreateChild(player.transform, "Visual root");
        Renderer bodyRenderer =
            visualRoot.gameObject.AddComponent<MeshRenderer>();
        Collider gameplayCollider =
            visualRoot.gameObject.AddComponent<CapsuleCollider>();

        // The stage the first-person item hangs on. Nothing hands it to the
        // effects any more, and this is here to prove it stays lit: it is the
        // one renderer a player can see of their own while they are in a box,
        // and switching it off is how a carried torch used to vanish.
        Transform viewmodelRoot =
            CreateChild(player.transform, "Local viewmodel root");
        Renderer viewmodelRenderer =
            viewmodelRoot.gameObject.AddComponent<MeshRenderer>();

        GameObject hitboxObject = new("Explicit hitbox");
        hitboxObject.transform.SetParent(player.transform, false);
        Collider hitboxCollider = hitboxObject.AddComponent<BoxCollider>();

        GameObject unrelatedObject = new("Unrelated future component");
        unrelatedObject.transform.SetParent(player.transform, false);
        Renderer unrelatedRenderer =
            unrelatedObject.AddComponent<MeshRenderer>();
        Collider unrelatedCollider =
            unrelatedObject.AddComponent<SphereCollider>();

        PlayerHidingEffects effects = new(
            body,
            visualRoot,
            new[] { gameplayCollider },
            new[] { hitboxCollider });

        effects.Apply(
            hidePlayerVisuals: true,
            disablePlayerColliders: true);

        Assert.That(bodyRenderer.enabled, Is.False);
        Assert.That(viewmodelRenderer.enabled, Is.True);
        Assert.That(gameplayCollider.enabled, Is.False);
        Assert.That(hitboxCollider.enabled, Is.False);
        Assert.That(unrelatedRenderer.enabled, Is.True);
        Assert.That(unrelatedCollider.enabled, Is.True);
        Assert.That(
            body.constraints,
            Is.EqualTo(RigidbodyConstraints.FreezeAll));

        effects.Restore();

        Assert.That(bodyRenderer.enabled, Is.True);
        Assert.That(viewmodelRenderer.enabled, Is.True);
        Assert.That(gameplayCollider.enabled, Is.True);
        Assert.That(hitboxCollider.enabled, Is.True);
        Assert.That(unrelatedRenderer.enabled, Is.True);
        Assert.That(unrelatedCollider.enabled, Is.True);
        Assert.That(
            body.constraints,
            Is.EqualTo(RigidbodyConstraints.FreezeRotation));
    }

    [Test]
    public void PlayerController_AppliesDeadZoneClampAndRuntimeSpeed()
    {
        GameObject player = Track(new GameObject("Movement player"));
        player.SetActive(false);
        player.AddComponent<Rigidbody>().useGravity = false;
        PlayerController controller = player.AddComponent<PlayerController>();
        player.AddComponent<PlayerOrchestrator>();
        player.SetActive(true);

        controller.SetDirection(new Vector2(0.01f, 0.01f));
        Assert.That(
            PlayModeTestReflection.GetField<Vector2>(controller, "direction"),
            Is.EqualTo(Vector2.zero));

        controller.SetDirection(new Vector2(4f, 3f));
        Vector2 normalized =
            PlayModeTestReflection.GetField<Vector2>(controller, "direction");
        Assert.That(normalized.magnitude, Is.EqualTo(1f).Within(0.001f));

        controller.SetSpeed(7.25f);
        Assert.That(controller.GetSpeed(), Is.EqualTo(7.25f));
    }

    [Test]
    public void PlayerPosture_ChangesCapsuleAndRejectsStandingIntoCeiling()
    {
        GameObject player = Track(new GameObject("Posture player"));
        player.SetActive(false);
        CapsuleCollider capsule = player.AddComponent<CapsuleCollider>();
        capsule.radius = 0.4f;
        capsule.height = 2f;
        capsule.center = Vector3.up;

        GameObject pivotObject = Track(new GameObject("Camera pivot"));
        pivotObject.transform.SetParent(player.transform, false);
        PlayerPostureController posture =
            player.AddComponent<PlayerPostureController>();
        posture.SetBodyCollider(capsule);
        posture.SetCameraPivot(pivotObject.transform);
        PlayerOrchestrator orchestrator = player.AddComponent<PlayerOrchestrator>();
        player.SetActive(true);
        orchestrator.Setup(isMultiplayer: false, isOwner: true);

        posture.SetCrouching(true);
        Assert.That(capsule.height, Is.EqualTo(1f).Within(0.001f));
        Assert.That(capsule.center.y, Is.EqualTo(0.5f).Within(0.001f));

        GameObject ceiling = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        ceiling.name = "Low ceiling";
        ceiling.transform.position = new Vector3(0f, 1.65f, 0f);
        ceiling.transform.localScale = new Vector3(2f, 0.2f, 2f);
        Physics.SyncTransforms();

        Assert.That(posture.HasStandingClearance(), Is.False);

        ceiling.SetActive(false);
        Physics.SyncTransforms();
        Assert.That(posture.HasStandingClearance(), Is.True);
    }

    [Test]
    public void PlayerPosture_DoesNotOverrideActiveHidingCameraPose()
    {
        GameObject player = Track(new GameObject("Hiding camera player"));
        player.SetActive(false);
        player.AddComponent<Rigidbody>().useGravity = false;
        CapsuleCollider capsule = player.AddComponent<CapsuleCollider>();
        capsule.radius = 0.4f;
        capsule.height = 2f;
        capsule.center = Vector3.up;

        GameObject cameraObject = Track(new GameObject("Hiding camera"));
        cameraObject.transform.SetParent(player.transform, false);
        CameraLook cameraLook = cameraObject.AddComponent<CameraLook>();
        PlayModeTestReflection.SetField(
            cameraLook,
            "playerTransform",
            player.transform);
        PlayModeTestReflection.SetField(
            cameraLook,
            "lockCursorOnLocalControl",
            false);

        PlayerPostureController posture =
            player.AddComponent<PlayerPostureController>();
        posture.SetBodyCollider(capsule);
        posture.SetCameraPivot(cameraObject.transform);
        PlayModeTestReflection.SetField(
            posture,
            "cameraHeightSmoothTime",
            0f);

        PlayerOrchestrator orchestrator =
            player.AddComponent<PlayerOrchestrator>();
        player.SetActive(true);
        orchestrator.Setup(isMultiplayer: false, isOwner: true);
        cameraLook.SetLocalControl(true);

        GameObject anchorObject = Track(new GameObject("Hiding anchor"));
        anchorObject.transform.SetPositionAndRotation(
            new Vector3(8f, 3f, -4f),
            Quaternion.Euler(5f, 120f, 0f));

        Assert.That(
            cameraLook.TrySetHidingView(
                anchorObject.transform,
                -55f,
                55f,
                -35f,
                45f,
                allowPeeking: true),
            Is.True);

        Vector3 anchorPosition = anchorObject.transform.position;
        PlayModeTestReflection.Invoke(posture, "LateUpdate");

        Assert.That(
            Vector3.Distance(cameraObject.transform.position, anchorPosition),
            Is.LessThan(0.001f),
            "Posture camera height must not compete with the hiding anchor.");

        cameraLook.ClearHidingView();
        PlayModeTestReflection.Invoke(posture, "LateUpdate");

        Assert.That(
            cameraObject.transform.localPosition.y,
            Is.EqualTo(0.75f).Within(0.001f),
            "Posture control must resume after leaving the hiding view.");
    }

    [Test]
    public void HidingVignette_ShowsBehindHudAndNeverBlocksInput()
    {
        GameObject player = Track(new GameObject("Vignette player"));
        PlayerHidingVignette vignette =
            player.AddComponent<PlayerHidingVignette>();
        HidingPlaceData settings = Track(
            ScriptableObject.CreateInstance<HidingPlaceData>());
        PlayModeTestReflection.SetField(
            settings,
            "hidingVignetteOpacity",
            0.65f);
        PlayModeTestReflection.SetField(
            settings,
            "hidingVignetteFadeDuration",
            0f);

        vignette.Show(settings);

        Assert.That(vignette.IsVisible, Is.True);
        Assert.That(vignette.CurrentOpacity, Is.EqualTo(0.65f));

        Canvas canvas = player.GetComponentInChildren<Canvas>(true);
        CanvasGroup group = player.GetComponentInChildren<CanvasGroup>(true);
        RawImage image = player.GetComponentInChildren<RawImage>(true);

        Assert.That(canvas, Is.Not.Null);
        Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
        Assert.That(canvas.sortingOrder, Is.LessThan(0));
        Assert.That(group.blocksRaycasts, Is.False);
        Assert.That(group.interactable, Is.False);
        Assert.That(image.raycastTarget, Is.False);
        Assert.That(image.texture, Is.Not.Null);

        vignette.Hide(fadeDuration: 0f);

        Assert.That(vignette.IsVisible, Is.False);
        Assert.That(vignette.CurrentOpacity, Is.Zero);
        Assert.That(canvas.enabled, Is.False);
    }

    private static Transform CreateChild(Transform parent, string name)
    {
        GameObject child = new(name);
        child.transform.SetParent(parent, false);
        return child.transform;
    }

    private T Track<T>(T value)
        where T : Object
    {
        cleanup.Add(value);
        return value;
    }

    // A second of flight covers the same ground at any frame rate. The body
    // interpolates between physics steps and used to write its own pose over
    // every frame's move but the last before each step, so the faster the
    // game ran, the slower noclip flew.
    [UnityTest]
    public IEnumerator NoClip_FliesAsFastAtAnyFrameRate([Values(30, 144)] int framesPerSecond)
    {
        GameObject body = new("Noclip body");
        body.SetActive(false);
        cleanup.Add(body);
        body.AddComponent<Rigidbody>().interpolation = RigidbodyInterpolation.Interpolate;
        PlayerController player = body.AddComponent<PlayerController>();
        player.enabled = false;
        body.SetActive(true);

        NoClipController noClip = new();
        Assert.That(noClip.SetEnabled(player, true), Is.True);

        int previousCaptureFramerate = Time.captureFramerate;
        Time.captureFramerate = framesPerSecond;

        try
        {
            yield return null;
            Vector3 start = body.transform.position;

            for (int frame = 0; frame < framesPerSecond; frame++)
            {
                noClip.Fly(Vector3.forward, Time.deltaTime);
                yield return null;
            }

            Assert.That(
                Vector3.Distance(start, body.transform.position),
                Is.EqualTo(noClip.Speed).Within(noClip.Speed * 0.1f),
                $"One second of noclip at {framesPerSecond} frames a second.");
        }
        finally
        {
            Time.captureFramerate = previousCaptureFramerate;
            noClip.Restore();
        }
    }

    // Up onto something low on the floor, and not up a wall. A body pushed by
    // its velocity used to stop dead at the first vertical face it met,
    // however low - the edge of a plank on the floor stopped a player.
    [UnityTest]
    public IEnumerator Player_StepsUpOntoSomethingLow_NotOntoAWall([Values(0.1f, 0.2f, 0.3f, 0.6f)] float height)
    {
        Solid("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(10f, 1f, 10f));
        Solid("Obstacle", new Vector3(0f, height * 0.5f, 2f), new Vector3(3f, height, 1f));

        PlayerController controller = WalkingPlayer(2f, out Rigidbody body);

        for (int step = 0; step < 120 && body.position.z < 2.1f; step++)
        {
            controller.SetDirection(Vector2.up);
            yield return new WaitForFixedUpdate();
        }

        string seen = $"ended at {body.position}";

        if (height <= 0.35f)
        {
            Assert.That(body.position.y, Is.EqualTo(height).Within(0.06f), "The player did not step up. " + seen);
            Assert.That(body.position.z, Is.GreaterThan(2f), "The player did not walk on over it. " + seen);
        }
        else
        {
            Assert.That(body.position.y, Is.LessThan(0.1f), "The player climbed a wall. " + seen);
            Assert.That(body.position.z, Is.LessThan(1.2f), "The player went through a wall. " + seen);
        }
    }

    // Already up against something low, standing, and then walking on: up
    // onto it, and never down on the way. The round bottom of the body only
    // grazes the edge of anything low, and the step used to look for it a
    // hair higher than the body stood - it passed over the edge without
    // touching it, and the player stayed where they were.
    [UnityTest]
    public IEnumerator Player_StandingAtTheEdgeOfSomethingLow_StepsUpWhenWalkingOn([Values(0.05f, 0.1f, 0.2f, 0.3f)] float height)
    {
        Solid("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(10f, 1f, 10f));
        Solid("Obstacle", new Vector3(0f, height * 0.5f, 2f), new Vector3(3f, height, 1f));

        // Where the round bottom of the body touches the edge.
        float reach = Mathf.Sqrt(0.25f - (0.5f - height) * (0.5f - height));
        PlayerController controller = WalkingPlayer(2f, out Rigidbody body, 1.5f - reach - 0.005f);

        for (int step = 0; step < 25; step++)
        {
            controller.SetDirection(Vector2.zero);
            yield return new WaitForFixedUpdate();
        }

        float fallen = 0f;
        float previous = body.position.y;

        for (int step = 0; step < 60 && body.position.z < 2.1f; step++)
        {
            controller.SetDirection(Vector2.up);
            yield return new WaitForFixedUpdate();

            if (body.position.y < previous)
                fallen += previous - body.position.y;

            previous = body.position.y;
        }

        string seen = $"ended at {body.position}, fell {fallen:F3} in all";
        Assert.That(body.position.y, Is.EqualTo(height).Within(0.03f), "The player did not step up. " + seen);
        Assert.That(body.position.z, Is.GreaterThan(2f), "The player did not walk on over it. " + seen);
        Assert.That(fallen, Is.LessThan(0.03f), "The player went up and came down again. " + seen);
    }

    // Up a flight of stairs, straight and at an angle, stopping halfway and
    // going on, without getting stuck, without dropping back, and at very
    // nearly the pace of walking on the flat: the round bottom of the body
    // used to meet every stair short of it and slide up its face. The round bottom of the body rests on two edges
    // at once on stairs and on no tread; the step used to be measured from
    // below the tread the body was on, came out too high and was refused, and
    // the player stopped until they turned.
    [UnityTest]
    public IEnumerator Player_WalksUpAFlightOfStairs([Values(0.18f, 0.3f, 0.34f)] float rise, [Values(0f, 35f, 60f)] float angle)
    {
        return WalkUpStairs(rise, angle, maxStepHeight: 0.35f);
    }

    // Stairs as tall as steps are allowed to be, come at a slant. The footing
    // was looked for every half right angle and just inside the edge of the
    // body: a tall stair met at a slant fell between two looks, the body ran
    // into it first and was turned along it - up only walking straight at it.
    [UnityTest]
    public IEnumerator Player_WalksUpTallStairsAtASlant([Values(0.6f, 0.9f)] float rise, [Values(35f, 60f)] float angle)
    {
        return WalkUpStairs(rise, angle, maxStepHeight: 1f);
    }

    private IEnumerator WalkUpStairs(float rise, float angle, float maxStepHeight)
    {
        const int steps = 6;
        const float tread = 0.28f;

        Solid("Floor", new Vector3(0f, -0.5f, 4f), new Vector3(40f, 1f, 16f));

        // Each step a block on to the landing at the top, the last of them the
        // landing itself.
        for (int i = 0; i < steps; i++)
        {
            float from = 1.5f + i * tread;
            const float to = 9f;
            float height = (i + 1) * rise;
            Solid($"Step {i + 1}", new Vector3(0f, height * 0.5f, (from + to) * 0.5f), new Vector3(40f, height, to - from));
        }

        PlayerController controller = WalkingPlayer(2f, out Rigidbody body);
        PlayModeTestReflection.SetField(controller, "maxStepHeight", maxStepHeight);
        Vector2 heading = new(Mathf.Sin(angle * Mathf.Deg2Rad), Mathf.Cos(angle * Mathf.Deg2Rad));
        float fallen = 0f;
        float previous = body.position.y;
        int frames = 0;
        bool paused = false;
        float fastestUp = 0f;

        while (body.position.z < 1.5f + steps * tread + 0.6f && frames < 250)
        {
            // Standing on the stairs, the body rests on the edges of two
            // treads and on neither of them.
            if (!paused && body.position.z > 1.5f + 3.5f * tread)
            {
                paused = true;

                for (int still = 0; still < 25; still++)
                {
                    controller.SetDirection(Vector2.zero);
                    yield return new WaitForFixedUpdate();
                }

                previous = body.position.y;
            }

            controller.SetDirection(heading);
            yield return new WaitForFixedUpdate();
            frames++;
            fastestUp = Mathf.Max(fastestUp, body.linearVelocity.y);

            if (body.position.y < previous)
                fallen += previous - body.position.y;

            previous = body.position.y;
        }

        string seen = $"ended at {body.position} after {frames} physics steps, fell {fallen:F3} in all";
        Assert.That(body.position.y, Is.EqualTo(steps * rise).Within(0.03f), "The player did not get up the stairs. " + seen);
        Assert.That(frames, Is.LessThan(150), "The player got stuck on the way up. " + seen);

        // Speeding up from standing twice - at the start and after the stop -
        // costs nine physics steps or so; the stairs themselves nothing. Going
        // up by the round of its bottom, the body lost one to six more.
        float flatFrames = body.position.z / (5f * heading.y * Time.fixedDeltaTime);
        Assert.That(frames, Is.LessThan(flatFrames + 10f), "The player slowed on the stairs. " + seen);

        // Put up each stair, not pushed up it.
        Assert.That(fastestUp, Is.LessThan(0.5f), "The player was shoved up the stairs. " + seen);
        Assert.That(fallen, Is.LessThan(0.05f), "The player dropped back on the way up. " + seen);
    }

    // Into a vent: a sill a step high, and a ceiling too low to stand under.
    // Upright the body does not fit and stays where it is, still; crouched it
    // goes up over the sill and in. The step used to put the body up from
    // before it reached the sill, so it fell back and was put up again -
    // and under a low ceiling the body never fitted where a thinner check
    // said it would. Either way the camera shook at the way in.
    [UnityTest]
    //
    // However high a step may be: with steps allowed a metre high, the body
    // was put up a metre before it looked ahead, met the top of the vent
    // and never went in. The vent is barely taller than the crouched body.
    public IEnumerator Player_IntoAVent_GoesInCrouched_AndStaysStillUpright(
        [Values(1f, 2f)] float bodyHeight,
        [Values(0.35f, 1f)] float maxStepHeight)
    {
        Solid("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(10f, 1f, 12f));
        Solid("Vent floor", new Vector3(0f, 0.1f, 3f), new Vector3(3f, 0.2f, 3f));
        Solid("Vent top", new Vector3(0f, 1.5f, 3f), new Vector3(3f, 0.5f, 3f));

        PlayerController controller = WalkingPlayer(bodyHeight, out Rigidbody body);
        PlayModeTestReflection.SetField(controller, "maxStepHeight", maxStepHeight);
        float highest = float.MinValue;
        float fallen = 0f;
        float previous = body.position.y;

        for (int step = 0; step < 120 && body.position.z < 3f; step++)
        {
            controller.SetDirection(Vector2.up);
            yield return new WaitForFixedUpdate();

            highest = Mathf.Max(highest, body.position.y);

            if (body.position.y < previous)
                fallen += previous - body.position.y;

            previous = body.position.y;
        }

        string seen = $"ended at {body.position}, highest {highest:F3}, fell {fallen:F3} in all";

        if (bodyHeight < 1.2f)
        {
            Assert.That(body.position.y, Is.EqualTo(0.2f).Within(0.05f), "Crouched, the body did not get up into the vent. " + seen);
            Assert.That(body.position.z, Is.GreaterThan(2f), "Crouched, the body did not go in. " + seen);
        }
        else
        {
            Assert.That(highest, Is.LessThan(0.05f), "Upright, the body was put up where it does not fit. " + seen);
        }

        Assert.That(fallen, Is.LessThan(0.05f), "The body went up and fell back: the camera shakes. " + seen);
    }

    private PlayerController WalkingPlayer(float height, out Rigidbody body, float z = 0f)
    {
        GameObject player = new("Walking player");
        cleanup.Add(player);
        player.SetActive(false);
        player.transform.position = new Vector3(0f, 0f, z);
        body = player.AddComponent<Rigidbody>();
        CapsuleCollider capsule = player.AddComponent<CapsuleCollider>();
        capsule.radius = 0.5f;
        capsule.height = height;
        capsule.center = Vector3.up * (height * 0.5f);
        PlayerController controller = player.AddComponent<PlayerController>();
        controller.SetBodyCollider(capsule);
        player.AddComponent<PlayerOrchestrator>();
        player.SetActive(true);
        player.GetComponent<PlayerOrchestrator>().Setup(isMultiplayer: false, isOwner: true);
        controller.SetDirection(Vector2.up);
        return controller;
    }

    private void Solid(string name, Vector3 position, Vector3 size)
    {
        GameObject solid = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cleanup.Add(solid);
        solid.name = name;
        solid.transform.position = position;
        solid.transform.localScale = size;
    }

    // The enemy walks into a player and stops; the player is not moved. It
    // used to walk the host out of its way - the one player whose body is
    // simulated on the server - while stopping against every guest.
    [UnityTest]
    public IEnumerator Enemy_CannotShoveAPlayer()
    {
        Vector3 gravity = Physics.gravity;
        Physics.gravity = Vector3.zero;

        Rigidbody player = Body("Player body", Vector3.zero, 5f);
        Rigidbody enemy = Body("Enemy body", new Vector3(0f, 0f, -1.2f), 30f);
        EnemyPlayerContacts.AddPlayer(player);
        EnemyPlayerContacts.AddEnemy(enemy);

        try
        {
            for (int step = 0; step < 50; step++)
            {
                enemy.linearVelocity = new Vector3(0f, 0f, 2f);
                yield return new WaitForFixedUpdate();
            }

            Assert.That(player.position.magnitude, Is.LessThan(0.05f),
                "The enemy shoved the player.");
            Assert.That(enemy.position.z, Is.LessThan(-0.9f),
                "The enemy walked through the player.");
        }
        finally
        {
            EnemyPlayerContacts.RemovePlayer(player);
            EnemyPlayerContacts.RemoveEnemy(enemy);
            Physics.gravity = gravity;
        }
    }

    private Rigidbody Body(string name, Vector3 position, float mass)
    {
        GameObject body = new(name);
        cleanup.Add(body);
        body.transform.position = position;
        body.AddComponent<CapsuleCollider>().radius = 0.5f;

        Rigidbody rigidbody = body.AddComponent<Rigidbody>();
        rigidbody.mass = mass;
        rigidbody.useGravity = false;
        rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
        return rigidbody;
    }
}
