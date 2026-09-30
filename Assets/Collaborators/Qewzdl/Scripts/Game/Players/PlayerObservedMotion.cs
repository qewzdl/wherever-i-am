using UnityEngine;

// The one ObservedMotion of a player, kept on the player. Asked for on first
// use rather than wired into the prefab, like the other leaves that read it.
[DisallowMultipleComponent]
public sealed class PlayerObservedMotion : MonoBehaviour
{
    private readonly ObservedMotion motion = new();

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
            motion.Sample(Time.frameCount, transform.position, Time.time);
            return motion;
        }
    }

    private void OnDisable()
    {
        motion.Reset();
    }
}
