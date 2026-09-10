using UnityEngine;

// Single read-only input handed to every ICameraEffect each frame. Effects never reach
// into Rigidbody/PlayerController themselves — PlayerCameraEffects collects this once and
// hands the same struct to all of them, so adding an effect never means threading a new
// reference through PlayerSetup.
public readonly struct CameraEffectContext
{
    public readonly float DeltaTime;
    public readonly Vector3 LocalVelocity;
    public readonly float HorizontalSpeed;
    public readonly Vector2 MoveInput;
    // Camera turn speed in degrees/second (signed — positive turns right), independent of
    // movement. Only Roll reads this; every other effect cares about the body, not the look.
    public readonly float YawRate;
    public readonly bool IsGrounded;
    public readonly bool IsCrouching;
    // True while the hiding view is active (in a wardrobe, under a bed, ...). Like IsCrouching
    // it is read by the stack, not by the effects — see ICameraEffect.HidingMultiplier.
    public readonly bool IsHiding;
    public readonly float Weight;

    public CameraEffectContext(
        float deltaTime,
        Vector3 localVelocity,
        float horizontalSpeed,
        Vector2 moveInput,
        float yawRate,
        bool isGrounded,
        bool isCrouching,
        bool isHiding,
        float weight)
    {
        DeltaTime = deltaTime;
        LocalVelocity = localVelocity;
        HorizontalSpeed = horizontalSpeed;
        MoveInput = moveInput;
        YawRate = yawRate;
        IsGrounded = isGrounded;
        IsCrouching = isCrouching;
        IsHiding = isHiding;
        Weight = weight;
    }

    // Copy of this context with a different Weight. The struct is readonly, so the stack uses
    // this to hand each effect its own per-effect weight without rebuilding the rest by hand.
    public CameraEffectContext WithWeight(float weight)
    {
        return new CameraEffectContext(
            DeltaTime,
            LocalVelocity,
            HorizontalSpeed,
            MoveInput,
            YawRate,
            IsGrounded,
            IsCrouching,
            IsHiding,
            weight);
    }
}
