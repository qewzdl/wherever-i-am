using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class ProductionBootstrapCi
{
    private const string TestDefine = "WIA_PRODUCTION_BOOTSTRAP_TEST";
    private const string StartGameSignal = "start-game.signal";
    private const string ShutdownSignal = "shutdown.signal";
    private const int DefaultStepTimeoutSeconds = 120;

    [MenuItem("Tools/Wherever I Am/Tests/Run Production Bootstrap")]
    public static void Run()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string artifactRoot = ResolveArtifactRoot(projectRoot);
        string runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
                       "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        string runDirectory = Path.Combine(artifactRoot, "results", runId);
        DateTime startedUtc = DateTime.UtcNow;
        List<PlayerProcess> players = new();
        List<RoleResultData> roleResults = new();
        Exception failure = null;

        Directory.CreateDirectory(runDirectory);
        WriteAtomic(
            Path.Combine(artifactRoot, "latest-run.txt"),
            runDirectory);

        try
        {
            string playerPath = BuildProductionBootstrapPlayer(projectRoot, artifactRoot);
            int timeoutSeconds = ReadTimeoutSeconds();

            PlayerProcess host = StartPlayer(
                playerPath,
                "host",
                runDirectory,
                timeoutSeconds);
            players.Add(host);
            WaitForMarker(runDirectory, "host.network.ready", players, timeoutSeconds);

            PlayerProcess client = StartPlayer(
                playerPath,
                "client",
                runDirectory,
                timeoutSeconds);
            players.Add(client);
            WaitForMarker(runDirectory, "host.lobby.ready", players, timeoutSeconds);
            WaitForMarker(runDirectory, "client.lobby.ready", players, timeoutSeconds);

            WriteAtomic(
                Path.Combine(runDirectory, StartGameSignal),
                DateTime.UtcNow.ToString("O"));
            WaitForMarker(runDirectory, "host.game.ready", players, timeoutSeconds);
            WaitForMarker(runDirectory, "client.game.ready", players, timeoutSeconds);

            PlayerProcess lateClient = StartPlayer(
                playerPath,
                "late-client",
                runDirectory,
                timeoutSeconds);
            players.Add(lateClient);
            WaitForMarker(runDirectory, "late-client.game.ready", players, timeoutSeconds);

            WriteAtomic(
                Path.Combine(runDirectory, ShutdownSignal),
                DateTime.UtcNow.ToString("O"));

            roleResults.Add(WaitForResult(host, runDirectory, players, timeoutSeconds));
            roleResults.Add(WaitForResult(client, runDirectory, players, timeoutSeconds));
            roleResults.Add(WaitForResult(lateClient, runDirectory, players, timeoutSeconds));

            ValidateRoleResults(roleResults);
            Debug.Log(
                $"Production bootstrap passed in " +
                $"{(DateTime.UtcNow - startedUtc).TotalSeconds:F1}s. " +
                $"Artifacts: {runDirectory}");
        }
        catch (Exception exception)
        {
            failure = exception;
            Debug.LogException(exception);
        }
        finally
        {
            StopRemainingPlayers(players);
            WriteReports(
                runDirectory,
                startedUtc,
                roleResults,
                failure);
            EditorUtility.ClearProgressBar();
        }

        if (failure != null)
        {
            throw new InvalidOperationException(
                $"Production bootstrap failed. Artifacts: {runDirectory}",
                failure);
        }
    }

    private static string BuildProductionBootstrapPlayer(
        string projectRoot,
        string artifactRoot)
    {
        BuildTarget target;
        string executableName;

#if UNITY_EDITOR_WIN
        target = BuildTarget.StandaloneWindows64;
        executableName = "WhereverIAm-ProductionBootstrap.exe";
#elif UNITY_EDITOR_LINUX
        target = BuildTarget.StandaloneLinux64;
        executableName = "WhereverIAm-ProductionBootstrap.x86_64";
#else
        throw new PlatformNotSupportedException(
            "Production bootstrap CI supports Windows and Linux Unity Editors.");
#endif

        string buildDirectory = Path.Combine(artifactRoot, "player");
        string playerPath = Path.Combine(buildDirectory, executableName);
        Directory.CreateDirectory(buildDirectory);

        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0 ||
            !string.Equals(
                scenes[0],
                "Assets/Collaborators/Qewzdl/Scenes/Bootstrap.unity",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The production Bootstrap scene must be the first enabled Build Settings scene.");
        }

        EditorUtility.DisplayProgressBar(
            "Production Bootstrap",
            "Building test-only Development Player...",
            0.05f);

        BuildPlayerOptions options = new()
        {
            scenes = scenes,
            locationPathName = playerPath,
            target = target,
            options = BuildOptions.Development,
            extraScriptingDefines = new[] { TestDefine }
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);

        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Development Player build failed with {report.summary.totalErrors} error(s).");
        }

        if (!File.Exists(playerPath))
        {
            throw new FileNotFoundException(
                "BuildPipeline reported success but the Player executable is missing.",
                playerPath);
        }

        return playerPath;
    }

    private static PlayerProcess StartPlayer(
        string playerPath,
        string role,
        string runDirectory,
        int timeoutSeconds)
    {
        string logPath = Path.Combine(runDirectory, $"{role}.log");
        string arguments = string.Join(
            " ",
            "-batchmode",
            "-nographics",
            "-logFile",
            QuoteArgument(logPath),
            "-gBootstrapRole",
            QuoteArgument(role),
            "-gBootstrapRunDirectory",
            QuoteArgument(runDirectory),
            "-gBootstrapTimeoutSeconds",
            timeoutSeconds.ToString(CultureInfo.InvariantCulture));

        ProcessStartInfo startInfo = new()
        {
            FileName = playerPath,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(playerPath) ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process process = Process.Start(startInfo);

        if (process == null)
            throw new InvalidOperationException($"Failed to start '{role}' Player process.");

        Debug.Log($"Started production bootstrap role '{role}' (PID {process.Id}).");
        return new PlayerProcess(role, logPath, process);
    }

    private static void WaitForMarker(
        string runDirectory,
        string marker,
        IReadOnlyList<PlayerProcess> players,
        int timeoutSeconds)
    {
        string markerPath = Path.Combine(runDirectory, marker);
        DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (!File.Exists(markerPath))
        {
            ThrowIfAnyPlayerExited(players, marker);

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out after {timeoutSeconds}s waiting for '{marker}'.");
            }

            UpdateProgress(marker, deadline, timeoutSeconds);
            Thread.Sleep(100);
        }
    }

    private static RoleResultData WaitForResult(
        PlayerProcess player,
        string runDirectory,
        IReadOnlyList<PlayerProcess> players,
        int timeoutSeconds)
    {
        string resultFile = $"{player.Role}.result.json";
        WaitForMarker(runDirectory, resultFile, players, timeoutSeconds);
        string resultPath = Path.Combine(runDirectory, resultFile);
        RoleResultData result = JsonUtility.FromJson<RoleResultData>(
            File.ReadAllText(resultPath));

        if (result == null)
            throw new InvalidDataException($"Could not parse '{resultPath}'.");

        if (!player.Process.WaitForExit(10000))
        {
            throw new TimeoutException(
                $"Player '{player.Role}' wrote a result but did not exit.");
        }

        if (!result.succeeded || player.Process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Player '{player.Role}' failed with exit code " +
                $"{player.Process.ExitCode}: {result.message}\n" +
                ReadLogTail(player.LogPath));
        }

        return result;
    }

    private static void ValidateRoleResults(IReadOnlyList<RoleResultData> results)
    {
        if (results.Count != 3)
            throw new InvalidOperationException("Expected Host, Client and LateClient results.");

        ValidateRolePhases(
            results.Single(result => result.role == "host"),
            "main-menu",
            "network",
            "lobby",
            "game",
            "shutdown");
        ValidateRolePhases(
            results.Single(result => result.role == "client"),
            "main-menu",
            "network",
            "lobby",
            "game",
            "shutdown");

        RoleResultData lateClient =
            results.Single(result => result.role == "late-client");
        ValidateRolePhases(
            lateClient,
            "main-menu",
            "network",
            "game",
            "shutdown");

        if (lateClient.phases != null && lateClient.phases.Contains("lobby"))
        {
            throw new InvalidOperationException(
                "LateClient reported Lobby instead of direct Game synchronization.");
        }
    }

    private static void ValidateRolePhases(
        RoleResultData result,
        params string[] requiredPhases)
    {
        string[] phases = result.phases ?? Array.Empty<string>();

        for (int i = 0; i < requiredPhases.Length; i++)
        {
            if (!phases.Contains(requiredPhases[i]))
            {
                throw new InvalidOperationException(
                    $"Role '{result.role}' did not report phase '{requiredPhases[i]}'.");
            }
        }
    }

    private static void ThrowIfAnyPlayerExited(
        IReadOnlyList<PlayerProcess> players,
        string awaitedMarker)
    {
        for (int i = 0; i < players.Count; i++)
        {
            PlayerProcess player = players[i];

            if (!player.Process.HasExited)
                continue;

            string resultPath = Path.Combine(
                Path.GetDirectoryName(player.LogPath) ?? string.Empty,
                $"{player.Role}.result.json");

            if (File.Exists(resultPath))
            {
                RoleResultData result = JsonUtility.FromJson<RoleResultData>(
                    File.ReadAllText(resultPath));

                if (result != null &&
                    result.succeeded &&
                    player.Process.ExitCode == 0)
                {
                    continue;
                }
            }

            string resultMessage = File.Exists(resultPath)
                ? File.ReadAllText(resultPath)
                : "No role result was written.";

            throw new InvalidOperationException(
                $"Player '{player.Role}' exited with code {player.Process.ExitCode} " +
                $"before '{awaitedMarker}'. {resultMessage}\n" +
                ReadLogTail(player.LogPath));
        }
    }

    private static void StopRemainingPlayers(IReadOnlyList<PlayerProcess> players)
    {
        for (int i = 0; i < players.Count; i++)
        {
            Process process = players[i].Process;

            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Could not stop production bootstrap process " +
                    $"'{players[i].Role}': {exception.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void WriteReports(
        string runDirectory,
        DateTime startedUtc,
        IReadOnlyList<RoleResultData> roleResults,
        Exception failure)
    {
        bool succeeded = failure == null;
        DateTime finishedUtc = DateTime.UtcNow;
        RunSummaryData summary = new()
        {
            succeeded = succeeded,
            message = succeeded
                ? "Host, Client and LateClient completed the production bootstrap."
                : failure?.ToString() ?? "Unknown failure.",
            startedUtc = startedUtc.ToString("O"),
            finishedUtc = finishedUtc.ToString("O"),
            durationSeconds = (float)(finishedUtc - startedUtc).TotalSeconds,
            roles = roleResults.ToArray()
        };

        try
        {
            WriteAtomic(
                Path.Combine(runDirectory, "summary.json"),
                JsonUtility.ToJson(summary, true));
            WriteNUnitReport(
                Path.Combine(runDirectory, "ProductionBootstrapResults.xml"),
                summary);
        }
        catch (Exception reportException)
        {
            Debug.LogException(reportException);
        }
    }

    private static void WriteNUnitReport(string path, RunSummaryData summary)
    {
        XmlWriterSettings settings = new()
        {
            Indent = true,
            Encoding = new UTF8Encoding(false)
        };

        using XmlWriter writer = XmlWriter.Create(path, settings);
        string result = summary.succeeded ? "Passed" : "Failed";
        string total = "1";
        string passed = summary.succeeded ? "1" : "0";
        string failed = summary.succeeded ? "0" : "1";

        writer.WriteStartDocument();
        writer.WriteStartElement("test-run");
        writer.WriteAttributeString("id", "production-bootstrap");
        writer.WriteAttributeString("testcasecount", total);
        writer.WriteAttributeString("total", total);
        writer.WriteAttributeString("passed", passed);
        writer.WriteAttributeString("failed", failed);
        writer.WriteAttributeString("result", result);
        writer.WriteAttributeString(
            "duration",
            summary.durationSeconds.ToString("F3", CultureInfo.InvariantCulture));
        writer.WriteStartElement("test-suite");
        writer.WriteAttributeString("type", "Assembly");
        writer.WriteAttributeString("name", "ProductionBootstrap");
        writer.WriteAttributeString("result", result);
        writer.WriteStartElement("test-case");
        writer.WriteAttributeString("name", "HostClientLateClientLifecycle");
        writer.WriteAttributeString("fullname", "ProductionBootstrap.HostClientLateClientLifecycle");
        writer.WriteAttributeString("result", result);
        writer.WriteAttributeString(
            "duration",
            summary.durationSeconds.ToString("F3", CultureInfo.InvariantCulture));

        if (!summary.succeeded)
        {
            writer.WriteStartElement("failure");
            writer.WriteElementString("message", summary.message);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void UpdateProgress(
        string marker,
        DateTime deadline,
        int timeoutSeconds)
    {
        double remaining = Math.Max(0d, (deadline - DateTime.UtcNow).TotalSeconds);
        float progress = 1f - (float)(remaining / timeoutSeconds);
        EditorUtility.DisplayProgressBar(
            "Production Bootstrap",
            $"Waiting for {marker}...",
            Mathf.Clamp01(progress));
    }

    private static int ReadTimeoutSeconds()
    {
        string value = Environment.GetEnvironmentVariable(
            "WIA_BOOTSTRAP_TIMEOUT_SECONDS");

        return int.TryParse(value, out int timeout) && timeout >= 30
            ? timeout
            : DefaultStepTimeoutSeconds;
    }

    private static string ResolveArtifactRoot(string projectRoot)
    {
        string configured = Environment.GetEnvironmentVariable(
            "WIA_BOOTSTRAP_ARTIFACTS");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(projectRoot, "artifacts", "production-bootstrap")
            : Path.GetFullPath(configured);
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static string ReadLogTail(string path)
    {
        if (!File.Exists(path))
            return $"Log file is missing: {path}";

        string[] lines = File.ReadAllLines(path);
        int start = Math.Max(0, lines.Length - 80);
        return string.Join(Environment.NewLine, lines.Skip(start));
    }

    private static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        string temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, content);

        if (File.Exists(path))
            File.Delete(path);

        File.Move(temporaryPath, path);
    }

    private sealed class PlayerProcess
    {
        internal PlayerProcess(string role, string logPath, Process process)
        {
            Role = role;
            LogPath = logPath;
            Process = process;
        }

        internal string Role { get; }
        internal string LogPath { get; }
        internal Process Process { get; }
    }

    [Serializable]
    private sealed class RoleResultData
    {
        public string role;
        public bool succeeded;
        public string message;
        public string exception;
        public string unityVersion;
        public float durationSeconds;
        public string[] phases;
    }

    [Serializable]
    private sealed class RunSummaryData
    {
        public bool succeeded;
        public string message;
        public string startedUtc;
        public string finishedUtc;
        public float durationSeconds;
        public RoleResultData[] roles;
    }
}
