using UnityEngine;

[CreateAssetMenu(fileName = "SceneAudioRegistry", menuName = "Wherever I Am/Audio/Scenes/Scene Audio Registry")]
public class SceneAudioRegistry : ScriptableObject
{
    [Header("Profiles")]
    [SerializeField] private SceneAudioProfile[] profiles;

    [Header("Fallback")]
    [SerializeField] private SceneAudioProfile fallbackProfile;

    public SceneAudioProfile GetProfileForScene(string sceneName)
    {
        if (profiles != null)
        {
            for (int i = 0; i < profiles.Length; i++)
            {
                SceneAudioProfile profile = profiles[i];

                if (profile == null) continue;

                if (profile.MatchesScene(sceneName))
                {
                    return profile;
                }
            }
        }

        return fallbackProfile;
    }
}
