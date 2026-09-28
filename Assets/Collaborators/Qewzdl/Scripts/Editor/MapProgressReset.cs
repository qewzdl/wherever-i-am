using System.Diagnostics;
using UnityEditor;
using UnityEngine;

// Locks every map past the first again, by forgetting which maps have been
// won - on this machine, in both places that remember it: the editor's own
// saves, and the saves of a build of this project, which are somewhere else.
//
// The build's are deleted with reg.exe rather than through PlayerPrefs, which
// in the editor only ever reaches the editor's. Windows only: that is where
// the registry is, and where this project is built.
public static class MapProgressReset
{
    // Beside the map manager, a step apart from the windows above it.
    [MenuItem("Tools/Wherever I Am/Reset Map Progress", false, 111)]
    private static void ResetFromMenu()
    {
        if (!EditorUtility.DisplayDialog(
                "Reset Map Progress",
                "Forget which maps have been won, in the editor and in builds on this " +
                "machine? Every map after the first will be locked again.",
                "Reset",
                "Cancel"))
        {
            return;
        }

        PlayerPrefs.DeleteKey(MapProgress.Key);
        PlayerPrefs.Save();

        string build = ResetBuild();

        EditorUtility.DisplayDialog(
            "Reset Map Progress",
            "Map progress is reset in the editor.\n" + build,
            "OK");
    }

    // A build keeps each PlayerPrefs entry in the registry under
    // HKCU\Software\<company>\<product>, its name followed by _h and Unity's
    // hash of that name.
    private static string ResetBuild()
    {
#if UNITY_EDITOR_WIN
        string key = $@"HKCU\Software\{PlayerSettings.companyName}\{PlayerSettings.productName}";
        string value = RegistryName(MapProgress.Key);

        ProcessStartInfo start = new("reg.exe", $"delete \"{key}\" /v \"{value}\" /f")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using Process reg = Process.Start(start);

        if (reg == null || !reg.WaitForExit(5000))
            return "Builds: could not reach the registry.";

        // reg.exe answers 1 when there was nothing there to delete.
        return reg.ExitCode == 0
            ? "It is reset for builds too."
            : "Builds had no progress to reset.";
#else
        return "Builds keep theirs outside the registry on this system; reset them from the build.";
#endif
    }

    public static string RegistryName(string prefsKey)
    {
        uint hash = 5381;

        foreach (char c in prefsKey)
            hash = (hash * 33) ^ c;

        return $"{prefsKey}_h{hash}";
    }
}
