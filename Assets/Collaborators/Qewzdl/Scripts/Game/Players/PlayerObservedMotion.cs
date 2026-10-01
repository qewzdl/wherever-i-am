using UnityEngine;

// The one ObservedMotion of a player, kept on the player. Asked for on first
// use rather than wired into the prefab, like the other leaves that read it.
[DisallowMultipleComponent]
public sealed class PlayerObservedMotion : MonoBehaviour
{
    // A player is only ever put somewhere, rather than walked there, by a
    // hiding place: in, out, or pulled out by the enemy. A jump of a metre or
    // two in one frame read as a sprint, and the first stride of a sprint is
    // heard at once - so climbing into a cupboard sounded a running footstep
    // on every machine, and the enemy heard it, from the cupboard.
    //
    // Watched for this long after going into or out of hiding, as well as
    // throughout it. On somebody else's machine the new state and the jump
    // arrive separately, a tick or so apart, in either order.
    public const float SettleSeconds = 0.25f;

    private readonly ObservedMotion motion = new();
    private IReplicatedPlayerHidingStateService hiding;
    private bool hidingResolved;
    private bool wasInHiding;
    private float stillUntil = float.NegativeInfinity;

    public static PlayerObservedMotion On(GameObject player)
    {
        return player.TryGetComponent(out PlayerObservedMotion found)
            ? found
            : player.AddComponent<PlayerObservedMotion>();
    }

    public ObservedMotion Now
    {
        get
        {
            motion.Sample(Time.frameCount, transform.position, Time.time, IsBeingPlaced());
            return motion;
        }
    }

    private bool IsBeingPlaced()
    {
        if (!hidingResolved)
        {
            hiding = GetComponent<IReplicatedPlayerHidingStateService>();
            hidingResolved = true;
        }

        bool inHiding = hiding != null && hiding.IsInHidingSequence;

        if (inHiding != wasInHiding)
        {
            wasInHiding = inHiding;
            stillUntil = Time.time + SettleSeconds;
        }

        return inHiding || Time.time < stillUntil;
    }

    private void OnDisable()
    {
        motion.Reset();
    }
}
