using UnityEngine;

public sealed class ObjectiveMatchResultServiceInstaller : MonoBehaviour
{
    [Header("Required")]
    [SerializeField] private ObjectiveManager objectiveManager;
    [SerializeField] private NetworkGameFlow gameFlow;

    private bool installed;

    private void Awake()
    {
        Install();
    }

    private void Install()
    {
        if (installed)
        {
            return;
        }

        if (objectiveManager == null)
        {
            Debug.LogError($"{nameof(ObjectiveMatchResultServiceInstaller)} requires {nameof(ObjectiveManager)} reference.", this);
            enabled = false;
            return;
        }

        if (gameFlow == null)
        {
            Debug.LogError($"{nameof(ObjectiveMatchResultServiceInstaller)} requires {nameof(NetworkGameFlow)} reference.", this);
            enabled = false;
            return;
        }

        ObjectiveMatchResultService service = new ObjectiveMatchResultService();

        if (!service.Initialize(gameFlow, objectiveManager.ObjectiveConditions, this))
        {
            enabled = false;
            return;
        }

        if (!objectiveManager.ConfigureMatchResultService(service))
        {
            enabled = false;
            return;
        }

        installed = true;
    }
}