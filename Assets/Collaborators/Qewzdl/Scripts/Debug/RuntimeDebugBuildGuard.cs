using UnityEngine;

public static class RuntimeDebugBuildGuard
{
    public static bool IsEnabled => Application.isEditor || Debug.isDebugBuild;

    public static bool DestroyIfDisabled(Component component)
    {
        if (IsEnabled)
        {
            return false;
        }

        Object.Destroy(component);
        return true;
    }
}
