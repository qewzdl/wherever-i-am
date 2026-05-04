public interface IEnemyStateHandler
{
    EnemyState State { get; }
    void Enter();
    void Tick(float deltaTime);
    void Exit();
}