using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
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
        Assert.That(
            gate.TryBegin(PlayerActionKind.Hiding, hiding),
            Is.False);
        Assert.That(
            gate.End(PlayerActionKind.Pickup, drag),
            Is.False,
            "A different mechanic must not release the active action.");

        gate.Confirm(PlayerActionKind.Hiding, hiding);

        Assert.That(gate.ActiveAction, Is.EqualTo(PlayerActionKind.Hiding));
        Assert.That(
            gate.End(PlayerActionKind.Pickup, pickup),
            Is.False,
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
            new[] { hitboxCollider },
            viewmodelRoot);

        effects.Apply(
            hidePlayerVisuals: true,
            disablePlayerColliders: true);

        Assert.That(bodyRenderer.enabled, Is.False);
        Assert.That(viewmodelRenderer.enabled, Is.False);
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

    [UnityTest]
    public IEnumerator CameraFollow_SnapsAndSwitchesOffsetsForLocalControl()
    {
        GameObject target = Track(new GameObject("Camera target"));
        target.transform.position = new Vector3(2f, 3f, 4f);

        GameObject cameraObject = Track(new GameObject("Follow camera"));
        cameraObject.SetActive(false);
        CameraFollow follow = cameraObject.AddComponent<CameraFollow>();
        PlayModeTestReflection.SetField(follow, "target", target.transform);
        PlayModeTestReflection.SetField(follow, "positionSmoothTime", 0f);
        cameraObject.SetActive(true);

        follow.SetLocalControl(true);
        yield return null;

        Assert.That(
            Vector3.Distance(
                cameraObject.transform.position,
                target.transform.TransformPoint(new Vector3(0f, 0.491f, 0f))),
            Is.LessThan(0.001f));

        follow.SetCrouching(true);
        yield return null;

        Assert.That(
            Vector3.Distance(
                cameraObject.transform.position,
                target.transform.TransformPoint(new Vector3(0f, 0.2455f, 0f))),
            Is.LessThan(0.001f));

        follow.SetLocalControl(false);
        Assert.That(follow.enabled, Is.False);
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
}
