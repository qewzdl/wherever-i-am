using UnityEngine;

public interface IEnemyDirectMovementIntentSource
{
    bool TryGetEnemyDirectMovementIntent(
        out Vector3 destination,
        out float speed);
}
