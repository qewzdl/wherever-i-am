using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

[Category("Baseline")]
public sealed class HidingPlaceEditorSetupTests
{
    [Test]
    public void CreateInScene_BuildsCompleteUnderstandableAnchorRig()
    {
        HidingPlaceInteractable hidingPlace = null;
        HidingPlaceData data = null;

        try
        {
            hidingPlace = HidingPlaceSetupUtility.CreateInScene();
            GameObject root = hidingPlace.gameObject;

            Assert.That(root.GetComponent<NetworkObject>(), Is.Not.Null);
            Assert.That(root.GetComponent<Collider>(), Is.Not.Null);
            Assert.That(
                root.GetComponent<HidingPlacePresentation>(),
                Is.Not.Null
            );
            Assert.That(
                root.GetComponent<NetworkHidingGameplayNoiseEmitter>(),
                Is.Not.Null
            );

            SerializedObject serialized = new(hidingPlace);
            Transform interaction = GetTransform(
                serialized,
                HidingPlaceSetupUtility.InteractionAnchorProperty
            );
            Transform hiding = GetTransform(
                serialized,
                HidingPlaceSetupUtility.HidingPointProperty
            );
            Transform camera = GetTransform(
                serialized,
                HidingPlaceSetupUtility.CameraAnchorProperty
            );
            Transform exit = GetTransform(
                serialized,
                HidingPlaceSetupUtility.ExitPointProperty
            );
            SerializedProperty fallback = serialized.FindProperty(
                HidingPlaceSetupUtility.FallbackExitPointsProperty
            );

            Assert.That(interaction, Is.SameAs(root.transform));
            Assert.That(hiding.name, Is.EqualTo("Hiding Point"));
            Assert.That(camera.name, Is.EqualTo("Camera Anchor"));
            Assert.That(exit.name, Is.EqualTo("Exit Point"));
            Assert.That(fallback.arraySize, Is.EqualTo(2));
            Assert.That(exit.localPosition.z, Is.GreaterThan(0f));
            Assert.That(camera.localPosition.y, Is.GreaterThan(0f));

            for (int i = 0; i < fallback.arraySize; i++)
            {
                Transform fallbackExit = fallback
                    .GetArrayElementAtIndex(i)
                    .objectReferenceValue as Transform;

                Assert.That(fallbackExit, Is.Not.Null);
                Assert.That(
                    fallbackExit.parent,
                    Is.SameAs(root.transform)
                );
                Assert.That(
                    fallbackExit.gameObject.layer,
                    Is.EqualTo(root.layer)
                );
            }

            Assert.That(
                HidingPlaceSetupUtility.HasCompleteSetup(hidingPlace),
                Is.False,
                "A newly created hiding place must remain fail-closed " +
                "until its data asset is assigned."
            );

            data = ScriptableObject.CreateInstance<HidingPlaceData>();
            serialized.FindProperty("data").objectReferenceValue = data;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(
                HidingPlaceSetupUtility.HasCompleteSetup(hidingPlace),
                Is.True
            );
        }
        finally
        {
            if (hidingPlace != null)
            {
                Object.DestroyImmediate(hidingPlace.gameObject);
            }

            if (data != null)
            {
                Object.DestroyImmediate(data);
            }
        }
    }

    [Test]
    public void EnsureCompleteSetup_IsIdempotent_AndPreservesManualPlacement()
    {
        HidingPlaceInteractable hidingPlace = null;

        try
        {
            hidingPlace = HidingPlaceSetupUtility.CreateInScene();
            SerializedObject serialized = new(hidingPlace);
            Transform hidingPoint = GetTransform(
                serialized,
                HidingPlaceSetupUtility.HidingPointProperty
            );
            int childCount = hidingPlace.transform.childCount;
            Vector3 manualPosition = new(0.2f, 0.3f, -0.1f);
            hidingPoint.localPosition = manualPosition;

            HidingPlaceSetupUtility.EnsureCompleteSetup(
                hidingPlace,
                repositionExistingAnchors: false
            );

            Assert.That(
                hidingPlace.transform.childCount,
                Is.EqualTo(childCount),
                "Repeated setup created duplicate anchors."
            );
            Assert.That(hidingPoint.localPosition, Is.EqualTo(manualPosition));

            HidingPlaceAnchorLayout expected =
                HidingPlaceSetupUtility.CalculateLayout(
                    hidingPlace.transform
                );
            HidingPlaceSetupUtility.EnsureCompleteSetup(
                hidingPlace,
                repositionExistingAnchors: true
            );

            Assert.That(
                Vector3.Distance(
                    hidingPoint.localPosition,
                    expected.HidingPoint
                ),
                Is.LessThan(0.001f)
            );
        }
        finally
        {
            if (hidingPlace != null)
            {
                Object.DestroyImmediate(hidingPlace.gameObject);
            }
        }
    }

    private static Transform GetTransform(
        SerializedObject serialized,
        string propertyName
    )
    {
        Transform value = serialized
            .FindProperty(propertyName)
            .objectReferenceValue as Transform;
        Assert.That(value, Is.Not.Null, propertyName);
        return value;
    }
}
