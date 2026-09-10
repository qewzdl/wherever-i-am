// Contract every camera effect (head bob, roll, breathing, landing dip, shake, FOV kick, ...)
// implements. An effect reads the shared CameraEffectContext and adds its contribution to
// the shared CameraEffectOutput — it never writes to a transform or Camera itself, so any
// number of effects can be stacked in any order without one clobbering another.
public interface ICameraEffect
{
    string DebugName { get; }

    // When false, CameraEffectStack skips Evaluate for this effect entirely — it contributes
    // nothing that frame. The effect stays registered and keeps its internal state (e.g.
    // Roll's current angle), so turning it back on resumes smoothly instead of popping in
    // from zero the way removing/re-adding it would.
    bool Enabled { get; set; }

    // How strongly this effect runs while the player is crouching (1 = full strength, 0 =
    // silent, >1 = amplified). CameraEffectStack folds it into the Weight it hands the effect,
    // so effects themselves never test IsCrouching — they already scale their output by Weight.
    float CrouchMultiplier { get; set; }

    // Same idea for the hiding view (wardrobe, under a bed): the stack folds it into Weight
    // whenever IsHiding is set. Crouching while hidden applies both multipliers.
    float HidingMultiplier { get; set; }

    void Evaluate(in CameraEffectContext context, ref CameraEffectOutput output);

    // Called on teleport, respawn, or entering/leaving a state (e.g. hiding) where an
    // effect's accumulated phase would otherwise produce a visible jump.
    void Reset();
}
