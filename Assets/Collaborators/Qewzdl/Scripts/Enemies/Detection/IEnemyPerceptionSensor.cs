public interface IEnemyPerceptionSensor
{
    bool TryFindBestStimulus(EnemyConfig config, out EnemyPerceptionStimulus stimulus);
}