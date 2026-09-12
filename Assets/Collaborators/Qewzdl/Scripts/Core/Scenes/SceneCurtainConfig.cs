using System;
using UnityEngine;

// Which scenes come up out of the dark.
//
// A list rather than a flag on each scene definition: this is a decision about
// how the game is presented, and it belongs where somebody looking for it
// would think to look - beside the other things that are tuned rather than
// buried in the scene registry, which is about where scenes live.
[CreateAssetMenu(
    fileName = "SceneCurtainConfig",
    menuName = "Wherever I Am/Scenes/Scene Curtain")]
public sealed class SceneCurtainConfig : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public ProjectSceneKind Scene;

        [Tooltip("How long the lightening takes on this scene. Three seconds " +
                 "reads as a room coming into the light; under one reads as a " +
                 "cut with a soft edge.")]
        [Min(0f)] public float Seconds;
    }

    [Tooltip("The scenes that open with the lightening, and how long each " +
             "takes. Anything not listed simply appears, which is right for " +
             "anywhere the player is passing through rather than arriving.")]
    [SerializeField] private Entry[] scenes = Array.Empty<Entry>();

    /// <summary>
    /// How long this scene takes to come up, or false if it does not.
    /// </summary>
    /// <remarks>
    /// One question rather than two. Asking whether a scene is covered and
    /// then how long it takes leaves room for the answer "covered, for no
    /// time at all", which is a scene that flashes black for a frame and looks
    /// like a fault.
    /// </remarks>
    public bool TryGetSeconds(ProjectSceneKind sceneKind, out float seconds)
    {
        seconds = 0f;

        if (sceneKind == ProjectSceneKind.Unknown || scenes == null)
            return false;

        for (int i = 0; i < scenes.Length; i++)
        {
            if (scenes[i].Scene != sceneKind)
                continue;

            seconds = Mathf.Max(0f, scenes[i].Seconds);
            return true;
        }

        return false;
    }

    public bool Covers(ProjectSceneKind sceneKind)
    {
        return TryGetSeconds(sceneKind, out _);
    }

#if UNITY_EDITOR
    public Entry[] ScenesForEditor => scenes;
#endif
}
