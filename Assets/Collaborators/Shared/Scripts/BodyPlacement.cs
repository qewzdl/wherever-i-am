using UnityEngine;

// Putting a body somewhere at once: out of a hiding place, down where an item
// is dropped, onto the navmesh of another posture.
//
// The same mistake was made here three times over. A transform written on a
// body that interpolates is taken back by the interpolation, so the body
// stayed where it was; a body written alone leaves its transform behind until
// the next physics step, so anything that looks this frame sees the old
// place. Both are written here, together, and whatever the body was doing is
// stopped unless the caller means it to carry on.
public static class BodyPlacement
{
    public static void Place(
        Transform root,
        Rigidbody body,
        Vector3 position,
        Quaternion rotation,
        bool stop = true)
    {
        if (body != null)
        {
            body.position = position;
            body.rotation = rotation;

            if (stop && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        if (root != null)
            root.SetPositionAndRotation(position, rotation);
    }
}
