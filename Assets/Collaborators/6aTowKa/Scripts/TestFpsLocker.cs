using UnityEngine;

public class TestFpsLocker : MonoBehaviour
{
    [SerializeField] private int frameRate = 60;

    void Awake()
    {
        // 1. Устанавливаем желаемый FPS
        Application.targetFrameRate = frameRate;

        // 2. Выключаем VSync (вертикальную синхронизацию), 
        // иначе она может игнорировать лимит и лочить под частоту монитора
        QualitySettings.vSyncCount = 0;
    }
}
