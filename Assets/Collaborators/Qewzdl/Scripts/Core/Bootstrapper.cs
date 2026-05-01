using UnityEngine;
using UnityEngine.SceneManagement;

public class Bootstrapper : MonoBehaviour
{
    private static Bootstrapper instance;

    [SerializeField] private string mainMenuSceneName = "Main Menu";
    [SerializeField] private GameStateMachine stateMachine;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        if (stateMachine == null) stateMachine = GetComponent<GameStateMachine>();
    }

    private void Start()
    {
        if (stateMachine == null)
        {
            Debug.LogError("GameStateMachine was not found on BootstrapManager.");
            return;
        }

        stateMachine.ChangeState(GameState.MainMenu);
        SceneManager.LoadScene(mainMenuSceneName, LoadSceneMode.Single);
    }
}