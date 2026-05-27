public interface IEnemyClientPresentation
{
    bool InitializePresentation(
        EnemyConfig enemyConfig,
        EnemyNetworkState enemyNetworkState,
        bool disableLocalNavigationAgent
    );

    void ShutdownPresentation();
}
