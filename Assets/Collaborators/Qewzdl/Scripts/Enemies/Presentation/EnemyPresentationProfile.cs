using UnityEngine;

[CreateAssetMenu(
    menuName = "Game/Enemies/Presentation/Enemy Presentation Profile",
    fileName = "EnemyPresentationProfile"
)]
public class EnemyPresentationProfile : ScriptableObject
{
    [Header("Animator Parameters")]
    [SerializeField] private string stateIntegerParameter = "EnemyState";
    [SerializeField] private bool useStateIntegerParameter = true;

    [Header("State Presentation")]
    [SerializeField] private EnemyStatePresentation[] states;

    public string StateIntegerParameter => stateIntegerParameter;
    public bool UseStateIntegerParameter => useStateIntegerParameter;

    public bool TryGetPresentation(
        EnemyState state,
        out EnemyStatePresentation presentation
    )
    {
        presentation = null;

        if (states == null)
        {
            return false;
        }

        for (int i = 0; i < states.Length; i++)
        {
            EnemyStatePresentation candidate = states[i];

            if (candidate == null || candidate.State != state)
            {
                continue;
            }

            presentation = candidate;
            return true;
        }

        return false;
    }
}