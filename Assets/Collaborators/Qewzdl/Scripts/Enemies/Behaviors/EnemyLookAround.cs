using UnityEngine;

// Stopping when she gets somewhere and turning her head both ways.
//
// Pulled out of the investigating state, where it was a third phase's worth of
// timer and three fields. It belongs out here because it is not about searching
// at all: it is about what a body does when it arrives somewhere it means to
// look at, and the investigation is only the thing that currently arranges for
// that to happen.
//
// Without it she walks her route without stopping. That is a faster search and
// a worse one - a walking vision cone drags past the corners a stationary one
// covers - which makes this a lever on how thorough an enemy is rather than a
// switch between working and broken.
public sealed class EnemyLookAround
{
    private readonly EnemyBrainContext context;

    private float timer;
    private float totalDuration;
    private Vector3 arrivalForward = Vector3.forward;

    public EnemyLookAround(EnemyBrainContext context)
    {
        this.context = context;
    }

    public bool IsLookingAround { get; private set; }

    // Has to be begun before anything repaths, because a repath re-issues the
    // destination on its next interval and undoes the stop.
    public bool TryBegin()
    {
        float duration = context.Config.investigationPointDwellDuration;

        if (duration <= 0f)
        {
            return false;
        }

        IsLookingAround = true;
        timer = duration;
        totalDuration = duration;
        arrivalForward = context.Navigator.transform.forward;
        context.StopNavigation();

        return true;
    }

    // True while she is still looking. False means she has finished and the
    // caller decides what happens next.
    public bool Tick(float deltaTime)
    {
        timer -= Mathf.Max(0f, deltaTime);

        if (timer > 0f)
        {
            TurnHead(deltaTime);
            return true;
        }

        IsLookingAround = false;
        timer = 0f;

        return false;
    }

    public void Reset()
    {
        IsLookingAround = false;
        timer = 0f;
        totalDuration = 0f;
        arrivalForward = Vector3.forward;
    }

    // First half of the pause turns one way, second half the other. A turn that
    // does not fit in its half simply stops short - she looks less far rather
    // than snapping round - so the angle, the speed and the duration can each
    // be tuned without the three having to agree exactly.
    private void TurnHead(float deltaTime)
    {
        float angle = context.Config.investigationLookAroundAngle;

        if (angle <= 0f || totalDuration <= 0f)
        {
            return;
        }

        bool isFirstHalf = timer > totalDuration * 0.5f;
        float targetYaw = isFirstHalf ? -angle : angle;

        context.Navigator.FaceDirection(
            Quaternion.Euler(0f, targetYaw, 0f) * arrivalForward,
            context.Config.investigationLookAroundSpeed,
            deltaTime
        );
    }
}
