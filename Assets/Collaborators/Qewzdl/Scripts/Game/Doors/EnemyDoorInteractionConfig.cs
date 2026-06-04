using UnityEngine;

[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Door Interaction Config",
    fileName = "EnemyDoorInteractionConfig"
)]
public sealed class EnemyDoorInteractionConfig : ScriptableObject
{
    [Header("Action")]
    [SerializeField] private EnemyDoorActionType defaultAction = EnemyDoorActionType.Open;
    [SerializeField] private bool allowBreakFallback = true;

    [Header("Timing")]
    [Min(0f)]
    [SerializeField] private float interactionDuration = 1.5f;

    [Min(0f)]
    [SerializeField] private float waitAfterInteractionDuration = 0.5f;

    [Header("Detection")]
    [Min(0.1f)]
    [SerializeField] private float detectionRadius = 2.5f;

    [Min(0.1f)]
    [SerializeField] private float interactionDistance = 1.1f;

    [Min(0.1f)]
    [SerializeField] private float pathHalfWidth = 1f;

    [SerializeField] private bool useRegisteredDoorZones = true;
    [SerializeField] private bool usePhysicsOverlapFallback = true;
    [SerializeField] private bool useDoorLayerMask;
    [SerializeField] private LayerMask doorLayerMask = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;

    public EnemyDoorActionType DefaultAction => defaultAction;
    public bool AllowBreakFallback => allowBreakFallback;
    public float InteractionDuration => Mathf.Max(0f, interactionDuration);
    public float WaitAfterInteractionDuration => Mathf.Max(0f, waitAfterInteractionDuration);
    public float DetectionRadius => Mathf.Max(0.1f, detectionRadius);
    public float InteractionDistance => Mathf.Max(0.1f, interactionDistance);
    public float PathHalfWidth => Mathf.Max(0.1f, pathHalfWidth);
    public bool UseRegisteredDoorZones => useRegisteredDoorZones;
    public bool UsePhysicsOverlapFallback => usePhysicsOverlapFallback;
    public bool UseDoorLayerMask => useDoorLayerMask;
    public LayerMask DoorLayerMask => doorLayerMask;
    public QueryTriggerInteraction TriggerInteraction => triggerInteraction;

    private void OnValidate()
    {
        interactionDuration = Mathf.Max(0f, interactionDuration);
        waitAfterInteractionDuration = Mathf.Max(0f, waitAfterInteractionDuration);
        detectionRadius = Mathf.Max(0.1f, detectionRadius);
        interactionDistance = Mathf.Max(0.1f, interactionDistance);
        pathHalfWidth = Mathf.Max(0.1f, pathHalfWidth);

        if (!useRegisteredDoorZones && !usePhysicsOverlapFallback)
        {
            useRegisteredDoorZones = true;
        }
    }
}
