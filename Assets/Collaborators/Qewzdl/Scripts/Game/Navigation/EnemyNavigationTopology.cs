using UnityEngine;

public static class EnemyNavigationTopology
{
    public static int Revision { get; private set; }

    public static void MarkChanged()
    {
        unchecked
        {
            Revision++;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Revision = 0;
    }
}
