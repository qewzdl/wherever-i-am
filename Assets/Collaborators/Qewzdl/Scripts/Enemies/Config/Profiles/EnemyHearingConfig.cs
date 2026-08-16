using UnityEngine;

[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Profiles/Enemy Hearing Config",
    fileName = "EnemyHearingConfig"
)]
public class EnemyHearingConfig : ScriptableObject
{
    public bool hearingEnabled = true;

    [Tooltip("The hard ceiling. However sharp the ears, nothing is heard past this.")]
    [Min(0f)] public float hearingRadius = 10f;

    [Tooltip(
        "How sharp the ears are, against how far a noise carries on its own. " +
        "1 hears each noise exactly as far as its own radius; 5 notices a sound " +
        "that carries three metres from fifteen. Turn this up for a harder " +
        "enemy - hearingRadius still caps the result.")]
    [Min(0.01f)] public float hearingSensitivity = 1f;

    [Min(0f)] public float hearingMemoryDuration = 3f;

    [Tooltip(
        "Noises quieter than this are never worth walking to, at any distance. " +
        "A floor for inaudible sources, not a difficulty knob - use " +
        "hearingSensitivity for that.")]
    [Min(0f)] public float minimumNoiseLoudness = 0.1f;

    public void Validate()
    {
        hearingRadius = Mathf.Max(0f, hearingRadius);
        hearingSensitivity = Mathf.Max(0.01f, hearingSensitivity);
        hearingMemoryDuration = Mathf.Max(0f, hearingMemoryDuration);
        minimumNoiseLoudness = Mathf.Max(0f, minimumNoiseLoudness);
    }

    private void OnValidate()
    {
        Validate();
    }
}