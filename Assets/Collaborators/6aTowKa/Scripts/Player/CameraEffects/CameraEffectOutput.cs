using UnityEngine;

// The only thing an ICameraEffect is allowed to produce. Effects never touch a transform or
// a Camera directly — they add to these three channels, and PlayerCameraEffects applies the
// accumulated result once. That is what lets bob, dip and shake add up instead of fighting
// over who wrote to the camera last.
public struct CameraEffectOutput
{
    public Vector3 PositionOffset;
    public Vector3 RotationOffset;
    public float FovOffset;

    public void Clear()
    {
        PositionOffset = Vector3.zero;
        RotationOffset = Vector3.zero;
        FovOffset = 0f;
    }
}
