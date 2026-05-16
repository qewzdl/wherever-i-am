using UnityEngine;

[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Profiles/Enemy Hearing Config",
    fileName = "EnemyHearingConfig"
)]
public class EnemyHearingConfig : ScriptableObject
{
    public bool hearingEnabled = true;
    [Min(0f)] public float hearingRadius = 10f;
    [Min(0f)] public float hearingMemoryDuration = 3f;
    [Min(0f)] public float minimumNoiseLoudness = 0.1f;

    public void Validate()
    {
        hearingRadius = Mathf.Max(0f, hearingRadius);
        hearingMemoryDuration = Mathf.Max(0f, hearingMemoryDuration);
        minimumNoiseLoudness = Mathf.Max(0f, minimumNoiseLoudness);
    }

    private void OnValidate()
    {
        Validate();
    }
}