using UnityEngine;

[System.Serializable]
public sealed class EnemyStatePresentation
{
    [SerializeField] private EnemyState state;

    [Header("Animator")]
    [SerializeField] private int animatorStateValue;
    [SerializeField] private string enterTrigger;
    [SerializeField] private bool resetTriggerOnExit;

    [Header("Audio")]
    [SerializeField] private SoundEffect enterSound;
    [SerializeField] private bool playSoundAtEnemyPosition = true;

    [Header("Threat")]
    [SerializeField] private EnemyThreatLevel threatLevel;

    public EnemyState State => state;
    public int AnimatorStateValue => animatorStateValue;
    public string EnterTrigger => enterTrigger;
    public bool ResetTriggerOnExit => resetTriggerOnExit;
    public SoundEffect EnterSound => enterSound;
    public bool PlaySoundAtEnemyPosition => playSoundAtEnemyPosition;
    public EnemyThreatLevel ThreatLevel => threatLevel;
}