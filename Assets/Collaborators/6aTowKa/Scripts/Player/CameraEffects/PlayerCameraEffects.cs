using UnityEngine;

// The composer: the single component allowed to write to the cosmetic camera transform
// (Main Camera, child of CameraPivot) and to Camera.fieldOfView. CameraPivot itself stays
// owned by CameraLook (rotation) and PlayerPostureController (crouch height) — this only
// ever touches its child, so it never races either of them regardless of execution order.
//
// Registered effects (ICameraEffect) never touch the transform or camera themselves; they
// add to a shared CameraEffectOutput each frame via CameraEffectStack, and this component
// applies the summed result once.
public class PlayerCameraEffects : PlayerComponent, IPlayerSignalListener, ISettingsServiceConsumer
{
    private const float DefaultFieldOfView = 75f;
    private const float MinFieldOfView = 50f;
    private const float MaxFieldOfView = 110f;

    [Header("References")]
    [SerializeField] private Transform effectsTarget;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private CameraLook cameraLook;
    [SerializeField] private Rigidbody playerRigidbody;
    [SerializeField] private PlayerController playerController;

    [Header("Weighting")]
    [Tooltip("Master strength for all effects combined (0 = off, 1 = authored, >1 = amplified). Crouch and hiding strength are per-effect, under each effect's own block.")]
    [SerializeField, Min(0f)] private float intensity = 1f;

    [Header("Effects")]
    [SerializeField] private RollSettings roll = new();
    [SerializeField] private HeadBobSettings headBob = new();
    [SerializeField] private StrafeLeanSettings strafeLean = new();
    [SerializeField] private BreathingSettings breathing = new();
    [SerializeField] private ShakeSettings shake = new();

    private readonly CameraEffectStack effectStack = new();
    private RollEffect rollEffect;
    private HeadBobFigureEightEffect headBobFigureEightEffect;
    private StrafeLeanEffect strafeLeanEffect;
    private BreathingEffect breathingEffect;
    private ShakeEffect shakeEffect;

    private ISettingsService settingsService;
    private float baseFieldOfView = DefaultFieldOfView;

    // Raised whenever something below changes: the inspector via OnValidate, or the player's
    // settings via SettingsChanged. The push itself waits for LateUpdate, because OnValidate
    // also fires on deserialisation and prefab load, long before the effects exist.
    private bool settingsDirty = true;

    private bool isCrouching;
    private bool listensToCrouchSync;

    private float previousCameraYaw;
    private bool hasPreviousCameraYaw;

    private float previousCameraPitch;
    private bool hasPreviousCameraPitch;

    private CameraEffectOutput lastOutput;
    private float lastYawRate;
    private float lastPitchRate;

    public CameraEffectOutput LastOutput => lastOutput;

    // Head turn speed this frame, degrees per second. Yaw feeds the roll effect; pitch is
    // not used by any effect here and exists for readers like ViewmodelSway, so the turn
    // rate comes from one place instead of being re-derived from the pivot elsewhere.
    public float LastYawRate => lastYawRate;
    public float LastPitchRate => lastPitchRate;

    protected override void OnPostInit(PlayerOrchestrator orch, bool isMultiplayer, bool isOwner)
    {
        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        signals.CrouchUpdateSignal.Listen(SetCrouching);

        if (isMultiplayer)
        {
            signals.CrouchSyncSignal.Listen(SetCrouching);
            listensToCrouchSync = true;
        }

        rollEffect = roll.CreateEffect();
        effectStack.Add(rollEffect);

        headBobFigureEightEffect = headBob.CreateEffect();
        effectStack.Add(headBobFigureEightEffect);

        strafeLeanEffect = strafeLean.CreateEffect();
        effectStack.Add(strafeLeanEffect);

        breathingEffect = breathing.CreateEffect();
        effectStack.Add(breathingEffect);

        shakeEffect = shake.CreateEffect();
        effectStack.Add(shakeEffect);
    }

    public void Cleanup()
    {
        if (signals == null)
        {
            listensToCrouchSync = false;
            return;
        }

        signals.CrouchUpdateSignal.Unlisten(SetCrouching);

        if (listensToCrouchSync)
            signals.CrouchSyncSignal.Unlisten(SetCrouching);

        listensToCrouchSync = false;
    }

    public void Construct(ISettingsService settings)
    {
        if (settings == null)
            throw new System.ArgumentNullException(nameof(settings));

        if (ReferenceEquals(settingsService, settings))
            return;

        ReleaseSettingsService();
        settingsService = settings;
        settingsService.FovChanged += OnFovChanged;
        settingsService.SettingsChanged += ApplySettings;
        ApplySettings();
    }

    public void ReleaseSettingsService()
    {
        if (settingsService == null)
            return;

        settingsService.FovChanged -= OnFovChanged;
        settingsService.SettingsChanged -= ApplySettings;
        settingsService = null;
    }

    // The entry point for shake, using the authored default length. Strength is a 0..1
    // fraction of the amplitudes below: roughly 0.2 for a thud nearby, 0.5 for a door
    // slammed next to the player, 1 for being grabbed.
    public void AddShake(float strength)
    {
        AddShake(strength, shake.defaultDuration);
    }

    // Same, with an explicit length in seconds. Overlapping impulses each keep their own,
    // so a long faint rumble can run underneath a short hard hit.
    public void AddShake(float strength, float duration)
    {
        if (shakeEffect == null)
            return;

        shakeEffect.AddImpulse(strength, duration);
    }

    // Registration is exposed for effects this component doesn't own itself — a future effect
    // owner (e.g. a scripted set piece) can register/unregister without this component knowing
    // about it.
    public void RegisterEffect(ICameraEffect effect)
    {
        effectStack.Add(effect);
    }

    public void UnregisterEffect(ICameraEffect effect)
    {
        effectStack.Remove(effect);
    }

    public void ResetEffects()
    {
        effectStack.ResetAll();
    }

    private void OnDestroy()
    {
        ReleaseSettingsService();
    }

    private void LateUpdate()
    {
        if (settingsDirty)
            PushSettings();

        float deltaTime = Time.deltaTime;

        Vector3 localVelocity = Vector3.zero;

        if (playerRigidbody != null)
            localVelocity = playerRigidbody.transform.InverseTransformDirection(playerRigidbody.linearVelocity);

        float horizontalSpeed = new Vector3(localVelocity.x, 0f, localVelocity.z).magnitude;

        bool isGrounded = playerController != null && playerController.IsGrounded;
        Vector2 moveInput = playerController != null ? playerController.MoveInput : Vector2.zero;
        lastYawRate = ComputeYawRate(deltaTime);
        lastPitchRate = ComputePitchRate(deltaTime);
        float yawRate = lastYawRate;

        // Crouch and hiding no longer touch this master weight — the stack folds each effect's
        // own multiplier in, so one effect can go quiet in a wardrobe while another does not.
        bool isHiding = cameraLook != null && cameraLook.IsHidingViewActive;

        CameraEffectContext context = new(
            deltaTime,
            localVelocity,
            horizontalSpeed,
            moveInput,
            yawRate,
            isGrounded,
            isCrouching,
            isHiding,
            intensity);

        lastOutput = effectStack.Evaluate(in context);

        effectsTarget.localPosition = lastOutput.PositionOffset;
        effectsTarget.localRotation = Quaternion.Euler(lastOutput.RotationOffset);
        targetCamera.fieldOfView = baseFieldOfView + lastOutput.FovOffset;
    }

    // Reads CameraPivot's world yaw rather than hooking into CameraLook's raw mouse delta —
    // that delta is consumed and zeroed inside CameraLook every frame. Comparing the world
    // angle frame-to-frame gives the same turn rate without reaching into CameraLook at all.
    private float ComputeYawRate(float deltaTime)
    {
        if (cameraLook == null)
            return 0f;

        float currentYaw = cameraLook.transform.eulerAngles.y;
        float yawRate = 0f;

        if (hasPreviousCameraYaw && deltaTime > 0f)
            yawRate = Mathf.DeltaAngle(previousCameraYaw, currentYaw) / deltaTime;

        previousCameraYaw = currentYaw;
        hasPreviousCameraYaw = true;

        return yawRate;
    }

    private float ComputePitchRate(float deltaTime)
    {
        if (cameraLook == null)
            return 0f;

        float currentPitch = cameraLook.transform.eulerAngles.x;
        float pitchRate = 0f;

        if (hasPreviousCameraPitch && deltaTime > 0f)
            pitchRate = Mathf.DeltaAngle(previousCameraPitch, currentPitch) / deltaTime;

        previousCameraPitch = currentPitch;
        hasPreviousCameraPitch = true;

        return pitchRate;
    }

    private void SetCrouching(bool value)
    {
        isCrouching = value;
    }

    private void OnFovChanged(float fieldOfView)
    {
        baseFieldOfView = Mathf.Clamp(fieldOfView, MinFieldOfView, MaxFieldOfView);
    }

#if UNITY_EDITOR
    // Unity raises this for any inspector edit on this component, play mode included. It is
    // the whole reason every block below can be tuned while the game runs: nothing is polled,
    // the editor says when something moved and the next frame hands it to the effects.
    //
    // The flip side is that it hears only the inspector. A field written from code would not
    // reach the effects until something else marked this dirty - nothing does that today, and
    // anything that starts to will have to say so.
    private void OnValidate()
    {
        settingsDirty = true;
    }
#endif

    // One place where the authored blocks meet the live effects. Called on a change rather
    // than every frame, so a build - where the inspector cannot be touched and the settings
    // service speaks in events - does this once at startup and then never again.
    private void PushSettings()
    {
        settingsDirty = false;

        roll.Apply(rollEffect);
        headBob.Apply(headBobFigureEightEffect);
        strafeLean.Apply(strafeLeanEffect);
        breathing.Apply(breathingEffect);
        shake.Apply(shakeEffect);
    }

    private void ApplySettings()
    {
        if (settingsService == null)
            return;

        GameSettingsData settings = settingsService.Current;

        baseFieldOfView = Mathf.Clamp(settings.fieldOfView, MinFieldOfView, MaxFieldOfView);

        // Reduced motion silences the whole stack at once. Head bob and the leans are exactly
        // what makes people motion-sick, so someone who ticked that box should not then have to
        // walk down five sliders by hand. The sliders keep their values while it is on and come
        // back untouched when it is off.
        if (settings.reducedMotion)
        {
            roll.userMultiplier = 0f;
            headBob.userMultiplier = 0f;
            strafeLean.userMultiplier = 0f;
            breathing.userMultiplier = 0f;
            shake.userMultiplier = 0f;

            settingsDirty = true;
            return;
        }

        roll.userMultiplier = Mathf.Clamp01(settings.cameraRollIntensity);
        headBob.userMultiplier = Mathf.Clamp01(settings.headBobIntensity);
        strafeLean.userMultiplier = Mathf.Clamp01(settings.strafeLeanIntensity);
        breathing.userMultiplier = Mathf.Clamp01(settings.breathingIntensity);
        shake.userMultiplier = Mathf.Clamp01(settings.cameraShakeIntensity);

        settingsDirty = true;
    }

    private bool ValidateReferences()
    {
        if (effectsTarget == null)
        {
            Debug.LogError($"{nameof(PlayerCameraEffects)} requires assigned {nameof(effectsTarget)}.", this);
            return false;
        }

        if (targetCamera == null)
        {
            Debug.LogError($"{nameof(PlayerCameraEffects)} requires assigned {nameof(targetCamera)}.", this);
            return false;
        }

        if (cameraLook == null)
        {
            Debug.LogError($"{nameof(PlayerCameraEffects)} requires assigned {nameof(cameraLook)}.", this);
            return false;
        }

        if (playerRigidbody == null)
        {
            Debug.LogError($"{nameof(PlayerCameraEffects)} requires assigned {nameof(playerRigidbody)}.", this);
            return false;
        }

        if (playerController == null)
        {
            Debug.LogError($"{nameof(PlayerCameraEffects)} requires assigned {nameof(playerController)}.", this);
            return false;
        }

        return true;
    }

    // Inspector blocks, one per effect. Nesting them lets each field carry its plain name
    // (verticalAmplitude, not headBobFigureEightVerticalAmplitude) and gives the inspector a
    // real foldout per effect instead of one flat wall of settings. They are read when the
    // effects are built and re-synced only for the live-tweakable flags below, so no part of
    // this is touched by the per-frame maths.
    [System.Serializable]
    public abstract class EffectSettings
    {
        [Tooltip("Turn the effect off without losing its tuning. Can be toggled in play mode.")]
        public bool enabled = true;
        [Tooltip("Effect strength while crouching (1 = unchanged, 0 = off, >1 = amplified).")]
        [Range(0f, 5f)] public float crouchMultiplier = 1f;
        [Tooltip("Effect strength while hidden in a wardrobe/under a bed (1 = unchanged, 0 = off, >1 = amplified). Stacks with the crouch multiplier when hiding while crouched.")]
        [Range(0f, 5f)] public float hidingMultiplier = 0.35f;

        // The player's slider for this effect, 0..1. Not serialized and not shown: the
        // inspector fields around it are the authored tuning, this is the player's choice on
        // top of it, and it arrives from the settings service every time the settings change.
        // Kept here rather than passed straight to the effect so it survives the effects being
        // rebuilt and rides along with the other live values below.
        [System.NonSerialized] public float userMultiplier = 1f;

        // The three fields above, which every effect has. Each block pairs this with its own
        // Apply, which adds the tuning particular to that effect.
        protected void ApplyLiveValues(ICameraEffect effect)
        {
            if (effect == null)
                return;

            // Снятая галка гасит эффект насовсем, а не ставит на паузу: стек перестаёт его
            // звать, поэтому всё, что он копил - фаза, огибающая, остаток тряски - замерло бы
            // и доиграло при включении обратно.
            if (effect.Enabled && !enabled)
                effect.Reset();

            effect.Enabled = enabled;
            effect.CrouchMultiplier = crouchMultiplier;
            effect.HidingMultiplier = hidingMultiplier;
            effect.UserMultiplier = userMultiplier;
        }
    }

    [System.Serializable]
    public sealed class RollSettings : EffectSettings
    {
        [Tooltip("Roll angle reached at yawRateForMaxRoll.")]
        [Min(0f)] public float maxAngleDegrees = 3f;
        [Tooltip("Mouse turn speed (deg/sec) at which roll caps out.")]
        [Min(1f)] public float yawRateForMaxRoll = 180f;
        [Tooltip("How quickly roll eases toward its target angle.")]
        [Min(0f)] public float smoothTime = 0.15f;

        public RollEffect CreateEffect()
        {
            RollEffect effect = new(maxAngleDegrees, yawRateForMaxRoll, smoothTime);
            Apply(effect);
            return effect;
        }

        public void Apply(RollEffect effect)
        {
            ApplyLiveValues(effect);

            if (effect == null)
                return;

            effect.ApplyTuning(maxAngleDegrees, yawRateForMaxRoll, smoothTime);
        }
    }

    [System.Serializable]
    public sealed class HeadBobSettings : EffectSettings
    {
        [Header("Position")]
        [Tooltip("Downward dip on each footfall (metres).")]
        [Min(0f)] public float verticalAmplitude = 0.02f;
        [Tooltip("Side-to-side sway, one full cycle per stride (metres).")]
        [Min(0f)] public float horizontalAmplitude = 0.015f;

        [Header("Rotation")]
        [Tooltip("Roll angle, in phase with the sway, on each step.")]
        [Min(0f)] public float rollDegrees = 1f;
        [Tooltip("Downward nod angle on each footfall, in phase with the dip.")]
        [Min(0f)] public float pitchDegrees = 0.6f;
        [Tooltip("Yaw angle, in phase with the sway, once per stride.")]
        [Min(0f)] public float yawDegrees = 0.5f;

        [Header("Rhythm")]
        [Tooltip("Stride cycles per metre travelled — higher means shorter, faster steps.")]
        [Min(0.01f)] public float frequency = 0.4f;
        [Tooltip("Same, but while crouching. Crouched strides are shorter, so this usually sits above the standing value.")]
        [Min(0.01f)] public float crouchFrequency = 0.6f;
        [Tooltip("Horizontal speed at which the bob reaches full amplitude.")]
        [Min(0.01f)] public float fullSpeed = 5f;
        [Tooltip("How quickly the bob fades in/out as the player starts and stops.")]
        [Min(0f)] public float envelopeSmoothTime = 0.15f;
        [Tooltip("Per-footfall random amplitude variation, ±this fraction (0 = perfectly repeating).")]
        [Range(0f, 1f)] public float stepJitterRange = 0.15f;
        [Tooltip("Fraction of each footfall spent dropping (rest is the slower recovery back up). LOWER = harder-hitting step; high values leave no room to recover and pop.")]
        [Range(0.05f, 0.6f)] public float impactFraction = 0.3f;
        [Tooltip("Make sway, roll and yaw follow the dip's fast-then-slow rhythm instead of an even glide.")]
        public bool swayFollowsImpact = true;

        public HeadBobFigureEightEffect CreateEffect()
        {
            HeadBobFigureEightEffect effect = new(
                verticalAmplitude,
                horizontalAmplitude,
                rollDegrees,
                pitchDegrees,
                yawDegrees,
                frequency,
                crouchFrequency,
                fullSpeed,
                envelopeSmoothTime,
                stepJitterRange,
                impactFraction,
                swayFollowsImpact);
            Apply(effect);
            return effect;
        }

        public void Apply(HeadBobFigureEightEffect effect)
        {
            ApplyLiveValues(effect);

            if (effect == null)
                return;

            effect.ApplyTuning(
                verticalAmplitude,
                horizontalAmplitude,
                rollDegrees,
                pitchDegrees,
                yawDegrees,
                frequency,
                crouchFrequency,
                fullSpeed,
                envelopeSmoothTime,
                stepJitterRange,
                impactFraction,
                swayFollowsImpact);
        }
    }

    [System.Serializable]
    public sealed class StrafeLeanSettings : EffectSettings
    {
        [Tooltip("Bank angle at a full sideways strafe. Diagonals lean proportionally less.")]
        [Min(0f)] public float maxAngleDegrees = 1.5f;
        [Tooltip("How far the head shifts sideways into the lean (metres).")]
        public float positionOffset = 0.02f;
        [Tooltip("How quickly the lean eases in and out as the strafe key goes down and up.")]
        [Min(0f)] public float smoothTime = 0.2f;

        public StrafeLeanEffect CreateEffect()
        {
            StrafeLeanEffect effect = new(maxAngleDegrees, positionOffset, smoothTime);
            Apply(effect);
            return effect;
        }

        public void Apply(StrafeLeanEffect effect)
        {
            ApplyLiveValues(effect);

            if (effect == null)
                return;

            effect.ApplyTuning(maxAngleDegrees, positionOffset, smoothTime);
        }
    }

    [System.Serializable]
    public sealed class BreathingSettings : EffectSettings
    {
        [Tooltip("Breaths per minute at rest.")]
        [Min(0.01f)] public float rate = 14f;
        [Tooltip("Vertical view travel across a full breath (metres).")]
        [Min(0f)] public float verticalAmplitude = 0.004f;
        [Tooltip("Pitch nod amplitude, in phase with the chest rise.")]
        [Min(0f)] public float pitchDegrees = 0.15f;
        [Tooltip("Slow roll on an unrelated rhythm so breathing doesn't read as one clean sine.")]
        [Min(0f)] public float swayRollDegrees = 0.1f;
        [Tooltip("Horizontal speed at which movement fully suppresses breathing.")]
        [Min(0.01f)] public float moveSuppressSpeed = 1.5f;
        [Tooltip("How quickly breathing fades in/out as the player stops and starts.")]
        [Min(0f)] public float suppressSmoothTime = 0.4f;

        public BreathingEffect CreateEffect()
        {
            BreathingEffect effect = new(
                rate,
                verticalAmplitude,
                pitchDegrees,
                swayRollDegrees,
                moveSuppressSpeed,
                suppressSmoothTime);
            Apply(effect);
            return effect;
        }

        public void Apply(BreathingEffect effect)
        {
            ApplyLiveValues(effect);

            if (effect == null)
                return;

            effect.ApplyTuning(
                rate,
                verticalAmplitude,
                pitchDegrees,
                swayRollDegrees,
                moveSuppressSpeed,
                suppressSmoothTime);
        }
    }

    [System.Serializable]
    public sealed class ShakeSettings : EffectSettings
    {
        [Header("Rotation")]
        [Tooltip("Pitch amplitude at full trauma.")]
        [Min(0f)] public float pitchDegrees = 2.5f;
        [Tooltip("Yaw amplitude at full trauma.")]
        [Min(0f)] public float yawDegrees = 2.5f;
        [Tooltip("Roll amplitude at full trauma.")]
        [Min(0f)] public float rollDegrees = 1.5f;

        [Header("Position")]
        [Tooltip("Sideways and vertical head travel at full trauma (metres).")]
        [Min(0f)] public float positionAmplitude = 0.03f;
        [Tooltip("Field-of-view wobble at full trauma (degrees). 0 leaves FOV alone.")]
        [Min(0f)] public float fovAmplitude = 0f;

        [Header("Response")]
        [Tooltip("Rattle rate in hertz — how fast the view vibrates, regardless of how hard.")]
        [Min(0.01f)] public float frequency = 18f;
        [Tooltip("Length in seconds used by callers that don't ask for one themselves.")]
        [Min(0.01f)] public float defaultDuration = 0.4f;
        [Tooltip("Shape of the fade within one impulse. 1 = even, higher = the hit spends itself up front and the tail is short.")]
        [Min(1f)] public float falloffExponent = 2f;

        // Hiding is exactly when a scare has to land, so shake keeps full strength in a
        // wardrobe instead of the muted default the ambient effects use.
        public ShakeSettings()
        {
            hidingMultiplier = 1f;
        }

        public ShakeEffect CreateEffect()
        {
            ShakeEffect effect = new(
                pitchDegrees,
                yawDegrees,
                rollDegrees,
                positionAmplitude,
                fovAmplitude,
                frequency,
                falloffExponent);
            Apply(effect);
            return effect;
        }

        public void Apply(ShakeEffect effect)
        {
            ApplyLiveValues(effect);

            if (effect == null)
                return;

            effect.ApplyTuning(
                pitchDegrees,
                yawDegrees,
                rollDegrees,
                positionAmplitude,
                fovAmplitude,
                frequency,
                falloffExponent);
        }
    }
}
