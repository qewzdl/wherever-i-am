public interface IEnemyPerceptionSensor : IEnemyValidatedComponent
{
    bool TryFindBestStimulus(EnemyConfig config, out EnemyPerceptionStimulus stimulus);
}