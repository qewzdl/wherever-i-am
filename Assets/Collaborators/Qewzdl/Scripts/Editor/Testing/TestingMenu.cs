using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Both acceptance runs build a Player and drive several real processes, so
// they are behind a confirmation that says what it is about to cost. The soak
// gets its short form here too: on CI that is an environment variable, and
// nobody should have to set one to spend ninety seconds instead of fifteen
// minutes.
internal static class TestingMenu
{
    private const string Root = "Tools/Wherever I Am/Tests/";
    private const string SmokeVariable = "WIA_NETWORK_SOAK_SMOKE";

    // Both runs drive the same built Player; ProductionBootstrapCi names the
    // executable and the soak borrows its build.
    private const string PlayerProcessName = "WhereverIAm-ProductionBootstrap";

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

        if (!EditorUtility.DisplayDialog(title, message, "Run", "Cancel"))
        {
            return false;
        }

        StopLeftoverPlayers();
        return true;
    }

    // A run that crashes or is killed never reaches its own cleanup, so its
    // player processes keep going and hold the LAN port the next one needs.
    // That next run then fails while starting the host, for a reason that has
    // nothing to do with what is being tested. The ci scripts clear them the
    // same way before starting; this is the other door into the same runs.
    private static void StopLeftoverPlayers()
    {
        Process[] leftovers = Process.GetProcessesByName(PlayerProcessName);

        for (int i = 0; i < leftovers.Length; i++)
        {
            using Process leftover = leftovers[i];

            try
            {
                leftover.Kill();
                leftover.WaitForExit(2000);
                Debug.Log($"Stopped a player process left behind by an earlier run (pid {leftover.Id}).");
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Could not stop a leftover player process: {exception.Message}");
            }
        }
    }

    private static string ProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}
