using UnityEngine;

public interface IEnemyPushNavigationIntentSource
{
    bool TryGetEnemyPushNavigationIntent(out Vector3 destination);
}
