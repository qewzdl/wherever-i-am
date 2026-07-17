using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

[Category("Gameplay")]
public sealed class EnemyTargetVisibilityPlayModeTests
{
    private readonly List<Object> cleanup = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = cleanup.Count - 1; i >= 0; i--)
        {
            if (cleanup[i] != null)
                Object.DestroyImmediate(cleanup[i]);
        }

        cleanup.Clear();
    }

    [Test]
    public void ExplicitVisibilityPoints_TakePriorityAndRespectCallerCapacity()
    {
        GameObject targetObject = Track(new GameObject("Explicit enemy target"));
        targetObject.SetActive(false);
        Transform first = CreatePoint("Eyes", targetObject.transform, new Vector3(1f, 2f, 3f));
        Transform second = CreatePoint("Chest", targetObject.transform, new Vector3(4f, 5f, 6f));
        ExpectInitialValidationBeforeTestComposition();
        EnemyTarget target = targetObject.AddComponent<EnemyTarget>();
        PlayModeTestReflection.SetField(
            target,
            "visibilityPoints",
            new[] { first, null, second });
        PlayModeTestReflection.SetField(target, "useColliderBoundsVisibility", false);
        targetObject.SetActive(true);

        Vector3[] results = new Vector3[1];
        int count = target.GetVisibilityPointsNonAlloc(results, 0f);

        Assert.That(count, Is.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(first.position));
        Assert.That(target.TryGetVisibilityBounds(out Bounds bounds), Is.True);
        Assert.That(bounds.Contains(first.position), Is.True);
        Assert.That(bounds.Contains(second.position), Is.True);
        Assert.That(target.AimPoint, Is.SameAs(targetObject.transform));
    }

    [Test]
    public void ColliderVisibility_ProducesStableBoundsSamplesAndIgnoresTriggers()
    {
        GameObject targetObject = Track(new GameObject("Collider enemy target"));
        targetObject.SetActive(false);
        BoxCollider solid = targetObject.AddComponent<BoxCollider>();
        solid.center = new Vector3(0f, 1f, 0f);
        solid.size = new Vector3(2f, 2f, 2f);

        GameObject triggerObject = Track(new GameObject("Trigger visibility"));
        triggerObject.SetActive(false);
        triggerObject.transform.SetParent(targetObject.transform, false);
        BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(100f, 100f, 100f);

        ExpectInitialValidationBeforeTestComposition();
        EnemyTarget target = targetObject.AddComponent<EnemyTarget>();
        PlayModeTestReflection.SetField(
            target,
            "visibilityColliders",
            new Collider[] { solid, trigger });
        PlayModeTestReflection.SetField(target, "includeTriggerColliders", false);
        triggerObject.SetActive(true);
        targetObject.SetActive(true);
        Physics.SyncTransforms();

        Vector3[] results = new Vector3[8];
        int count = target.GetVisibilityPointsNonAlloc(results, 1f);

        Assert.That(count, Is.EqualTo(8));
        Assert.That(target.TryGetVisibilityBounds(out Bounds bounds), Is.True);
        Assert.That(
            Vector3.Distance(bounds.size, new Vector3(2f, 2f, 2f)),
            Is.LessThan(0.001f));

        for (int i = 0; i < count; i++)
            Assert.That(bounds.Contains(results[i]), Is.True);
    }

    [Test]
    public void DetectionFlagAndAimPoint_AreExplicitConfiguration()
    {
        GameObject targetObject = Track(new GameObject("Configured enemy target"));
        targetObject.SetActive(false);
        BoxCollider collider = targetObject.AddComponent<BoxCollider>();
        Transform aim = CreatePoint(
            "Aim",
            targetObject.transform,
            new Vector3(0f, 1.5f, 0f));
        ExpectInitialValidationBeforeTestComposition();
        EnemyTarget target = targetObject.AddComponent<EnemyTarget>();
        PlayModeTestReflection.SetField(target, "aimPoint", aim);
        PlayModeTestReflection.SetField(
            target,
            "visibilityColliders",
            new Collider[] { collider });
        PlayModeTestReflection.SetField(target, "canBeDetected", false);
        targetObject.SetActive(true);

        Assert.That(target.CanBeDetected, Is.False);
        Assert.That(target.AimPoint, Is.SameAs(aim));
        Assert.That(target.AimPosition, Is.EqualTo(aim.position));
        Assert.That(target.IsValidNetworkTarget, Is.False);
    }

    private Transform CreatePoint(string name, Transform parent, Vector3 localPosition)
    {
        GameObject gameObject = Track(new GameObject(name));
        gameObject.transform.SetParent(parent, false);
        gameObject.transform.localPosition = localPosition;
        return gameObject.transform;
    }

    private static void ExpectInitialValidationBeforeTestComposition()
    {
        LogAssert.Expect(
            LogType.Error,
            new Regex("EnemyTarget has invalid visibility configuration:"));
    }

    private T Track<T>(T value)
        where T : Object
    {
        cleanup.Add(value);
        return value;
    }
}
