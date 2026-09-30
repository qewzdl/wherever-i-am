using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// A body put somewhere is there: at once, for anything looking this frame,
// and after the physics has run, for a body that interpolates.
public sealed class BodyPlacementPlayModeTests
{
    [UnityTest]
    public IEnumerator AnInterpolatedBody_IsWhereItWasPut_NowAndAfterPhysics()
    {
        GameObject thing = new("Placed body");

        try
        {
            Rigidbody body = thing.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.linearVelocity = new Vector3(3f, 0f, 0f);

            yield return new WaitForFixedUpdate();
            yield return null;

            Vector3 target = new(10f, 2f, -4f);
            BodyPlacement.Place(thing.transform, body, target, Quaternion.identity);

            Assert.That(Vector3.Distance(thing.transform.position, target), Is.LessThan(0.001f),
                "The transform was left behind for this frame.");

            for (int i = 0; i < 5; i++)
            {
                yield return new WaitForFixedUpdate();
                yield return null;
            }

            Assert.That(Vector3.Distance(thing.transform.position, target), Is.LessThan(0.001f),
                "The body did not stay where it was put.");
            Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero),
                "The body carried on at the pace it had.");
        }
        finally
        {
            Object.Destroy(thing);
        }
    }
}
