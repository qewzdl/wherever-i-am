using UnityEngine;
using UnityEngine.SceneManagement;

public class Bootstrapper : MonoBehaviour
{
    [SerializeField] private string mainMenuSceneName = "Main Menu";
    [SerializeField] private GameStateMachine stateMachine;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        stateMachine.ChangeState(GameState.MainMenu);
        SceneManager.LoadScene(mainMenuSceneName, LoadSceneMode.Single);
    }
}