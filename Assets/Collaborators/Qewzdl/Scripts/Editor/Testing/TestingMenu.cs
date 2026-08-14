using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Both acceptance runs build a Player and drive several real processes, so
// they are behind a confirmation that says what it is about to cost. The soak
// gets its short form here too: on CI that is an environment variable, and
// nobody should have to set one to spend ninety seconds instead of fifteen
// minutes.
internal static class TestingMenu
{
    private const string Root = "Tools/Wherever I Am/Tests/";
    private const string SmokeVariable = "WIA_NETWORK_SOAK_SMOKE";

    // Last, and a section of its own: everything here builds a Player and
    // takes minutes.
    private const int Priority = 160;

    [MenuItem(Root + "Run Network Soak (smoke, ~90 s)", false, Priority)]
    private static void RunNetworkSoakSmoke()
    {
        if (!Confirm(
                "Network soak (smoke)",
                "Builds a Player and runs the soak for about 90 seconds on a " +
                "simulated lossy link.\n\nThe editor is blocked until it finishes."))
        {
            return;
        }

        string previous = Environment.GetEnvironmentVariable(SmokeVariable);

        try
        {
            Environment.SetEnvironmentVariable(SmokeVariable, "1");
            NetworkSoakCi.Run();
        }
        finally
        {
            Environment.SetEnvironmentVariable(SmokeVariable, previous);
        }
    }

    [MenuItem(Root + "Run Network Soak (full, ~15 min)", false, Priority + 1)]
    private static void RunNetworkSoakFull()
    {
        if (!Confirm(
                "Network soak (full)",
                "Builds a Player and runs the soak for about 15 minutes, " +
                "cycling host, two clients, a fault during map load and a " +
                "reconnect.\n\nThe editor is blocked until it finishes."))
        {
            return;
        }

        NetworkSoakCi.Run();
    }

    [MenuItem(Root + "Run Production Bootstrap", false, Priority + 2)]
    private static void RunProductionBootstrap()
    {
        if (!Confirm(
                "Production bootstrap",
                "Builds a Player and drives three processes - host, client " +
                "and a late client - from bootstrap through a match to " +
                "shutdown.\n\nThe editor is blocked until it finishes."))
        {
            return;
        }

        ProductionBootstrapCi.Run();
    }

    [MenuItem(Root + "Open Test Artifacts", false, Priority + 20)]
    private static void OpenTestArtifacts()
    {
        string artifactRoot = Path.Combine(ProjectRoot(), "artifacts");

        if (!Directory.Exists(artifactRoot))
        {
            Debug.Log($"No test artifacts yet. They appear at '{artifactRoot}' after a run.");
            return;
        }

        EditorUtility.RevealInFinder(artifactRoot);
    }

    private static bool Confirm(string title, string message)
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning($"{title} cannot run while the editor is in play mode.");
            return false;
        }

        return EditorUtility.DisplayDialog(title, message, "Run", "Cancel");
    }

    private static string ProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}
