using UnityEngine;

public static class RuntimeLog
{
    public static bool InfoEnabled => Application.isEditor || Debug.isDebugBuild;

    public static void Info(string message)
    {
        if (!InfoEnabled)
            return;

        Debug.Log(message);
    }

    public static void Info(string message, Object context)
    {
        if (!InfoEnabled)
            return;

        Debug.Log(message, context);
    }
}
