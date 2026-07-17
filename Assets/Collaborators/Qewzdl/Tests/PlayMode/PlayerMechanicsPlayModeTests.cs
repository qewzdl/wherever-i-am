using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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

    private T Track<T>(T value)
        where T : Object
    {
        cleanup.Add(value);
        return value;
    }
}
