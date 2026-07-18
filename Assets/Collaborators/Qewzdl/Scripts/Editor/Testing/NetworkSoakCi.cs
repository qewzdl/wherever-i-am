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
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class NetworkSoakCi
{
    private const int FullDurationSeconds = 900;
    private const int SmokeDurationSeconds = 90;
    private const int DefaultStepTimeoutSeconds = 180;
    private const int DefaultLatencyMs = 80;
    private const int DefaultJitterMs = 20;
    private const float DefaultPacketLossPercent = 2f;

    [MenuItem(
        TestingMenu.Root + "Run Network Soak (15 min)",
        false,
        TestingMenu.Priority)]
    public static void Run()
    {
        bool smoke = ReadBoolean("WIA_NETWORK_SOAK_SMOKE");
        bool showPlayerWindows =
            ReadBoolean("WIA_NETWORK_SOAK_SHOW_WINDOWS") ||
            (!Application.isBatchMode &&
             !ReadBoolean("WIA_NETWORK_SOAK_HEADLESS"));
        int durationSeconds = ReadDurationSeconds(smoke);
        int stepTimeoutSeconds = ReadInteger(
            "WIA_NETWORK_SOAK_STEP_TIMEOUT_SECONDS",
            DefaultStepTimeoutSeconds,
            30,
            600);
        int latencyMs = ReadInteger(
            "WIA_NETWORK_SOAK_LATENCY_MS",
            DefaultLatencyMs,
            0,
            2000);
        int jitterMs = ReadInteger(
            "WIA_NETWORK_SOAK_JITTER_MS",
            DefaultJitterMs,
            0,
            1000);
        float packetLossPercent = ReadFloat(
            "WIA_NETWORK_SOAK_PACKET_LOSS_PERCENT",
            DefaultPacketLossPercent,
            0f,
            20f);

        string projectRoot =
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string artifactRoot =
            ResolveArtifactRoot(projectRoot);
        string runId =
            DateTime.UtcNow.ToString(
                "yyyyMMdd-HHmmss",
                CultureInfo.InvariantCulture) +
            "-" +
            Guid.NewGuid().ToString("N").Substring(0, 8);
        string runDirectory =
            Path.Combine(artifactRoot, "results", runId);
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
            string playerPath =
                ProductionBootstrapCi.BuildProductionBootstrapPlayer(
                    projectRoot,
                    artifactRoot);

            players.Add(StartPlayer(
                playerPath,
                "host",
                runDirectory,
                durationSeconds,
                stepTimeoutSeconds,
                latencyMs,
                jitterMs,
                packetLossPercent,
                showPlayerWindows));
            players.Add(StartPlayer(
                playerPath,
                "client-a",
                runDirectory,
                durationSeconds,
                stepTimeoutSeconds,
                latencyMs,
                jitterMs,
                packetLossPercent,
                showPlayerWindows));
            players.Add(StartPlayer(
                playerPath,
                "client-b",
                runDirectory,
                durationSeconds,
                stepTimeoutSeconds,
                latencyMs,
                jitterMs,
                packetLossPercent,
                showPlayerWindows));

            int overallTimeoutSeconds =
                durationSeconds +
                Math.Max(600, stepTimeoutSeconds * 3);

            for (int i = 0; i < players.Count; i++)
            {
                roleResults.Add(WaitForResult(
                    players[i],
                    runDirectory,
                    players,
                    overallTimeoutSeconds));
            }

            ValidateResults(roleResults, durationSeconds, smoke);
            Debug.Log(
                $"Network soak passed in " +
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
                durationSeconds,
                latencyMs,
                jitterMs,
                packetLossPercent,
                roleResults,
                failure);
            EditorUtility.ClearProgressBar();
        }

        if (failure != null)
        {
            throw new InvalidOperationException(
                $"Network soak failed. Artifacts: {runDirectory}",
                failure);
        }
    }

    private static PlayerProcess StartPlayer(
        string playerPath,
        string role,
        string runDirectory,
        int durationSeconds,
        int stepTimeoutSeconds,
        int latencyMs,
        int jitterMs,
        float packetLossPercent,
        bool showWindow)
    {
        string logPath =
            Path.Combine(runDirectory, $"{role}.log");
        List<string> playerArguments = new();

        if (showWindow)
        {
            playerArguments.Add("-screen-width");
            playerArguments.Add("960");
            playerArguments.Add("-screen-height");
            playerArguments.Add("540");
            playerArguments.Add("-screen-fullscreen");
            playerArguments.Add("0");
        }
        else
        {
            playerArguments.Add("-batchmode");
            playerArguments.Add("-nographics");
        }

        playerArguments.AddRange(new[]
        {
            "-logFile",
            QuoteArgument(logPath),
            "-gNetworkSoakRole",
            QuoteArgument(role),
            "-gNetworkSoakRunDirectory",
            QuoteArgument(runDirectory),
            "-gNetworkSoakDurationSeconds",
            durationSeconds.ToString(
                CultureInfo.InvariantCulture),
            "-gNetworkSoakStepTimeoutSeconds",
            stepTimeoutSeconds.ToString(
                CultureInfo.InvariantCulture),
            "-gNetworkSoakLatencyMs",
            latencyMs.ToString(
                CultureInfo.InvariantCulture),
            "-gNetworkSoakJitterMs",
            jitterMs.ToString(
                CultureInfo.InvariantCulture),
            "-gNetworkSoakPacketLossPercent",
            packetLossPercent.ToString(
                CultureInfo.InvariantCulture)
        });
        string arguments = string.Join(" ", playerArguments);

        ProcessStartInfo startInfo = new()
        {
            FileName = playerPath,
            Arguments = arguments,
            WorkingDirectory =
                Path.GetDirectoryName(playerPath) ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = !showWindow
        };
        Process process = Process.Start(startInfo);

        if (process == null)
        {
            throw new InvalidOperationException(
                $"Failed to start '{role}' soak Player.");
        }

        Debug.Log(
            $"Started network soak role '{role}' " +
            $"(PID {process.Id}, " +
            $"{(showWindow ? "windowed" : "headless")}).");
        return new PlayerProcess(role, logPath, process);
    }

    private static RoleResultData WaitForResult(
        PlayerProcess player,
        string runDirectory,
        IReadOnlyList<PlayerProcess> players,
        int timeoutSeconds)
    {
        string resultPath = Path.Combine(
            runDirectory,
            $"{player.Role}.result.json");
        DateTime deadline =
            DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (!File.Exists(resultPath))
        {
            ThrowIfAnyPlayerFailed(players, player.Role);

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out after {timeoutSeconds}s waiting " +
                    $"for '{player.Role}' soak result.");
            }

            double remaining =
                Math.Max(
                    0d,
                    (deadline - DateTime.UtcNow).TotalSeconds);
            float progress =
                1f - (float)(remaining / timeoutSeconds);
            EditorUtility.DisplayProgressBar(
                "Network Soak",
                $"Waiting for {player.Role}...",
                Mathf.Clamp01(progress));
            Thread.Sleep(250);
        }

        RoleResultData result =
            JsonUtility.FromJson<RoleResultData>(
                File.ReadAllText(resultPath));

        if (result == null)
        {
            throw new InvalidDataException(
                $"Could not parse '{resultPath}'.");
        }

        if (!player.Process.WaitForExit(10000))
        {
            throw new TimeoutException(
                $"Player '{player.Role}' wrote a result but did not exit.");
        }

        if (!result.succeeded ||
            player.Process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Player '{player.Role}' failed with exit code " +
                $"{player.Process.ExitCode}: {result.message}\n" +
                ReadLogTail(player.LogPath));
        }

        return result;
    }

    private static void ValidateResults(
        IReadOnlyList<RoleResultData> results,
        int requestedDurationSeconds,
        bool smoke)
    {
        if (results.Count != 3)
        {
            throw new InvalidOperationException(
                "Expected Host, Client A and Client B results.");
        }

        RoleResultData host =
            results.Single(result => result.role == "host");
        RoleResultData clientA =
            results.Single(result => result.role == "client-a");
        RoleResultData clientB =
            results.Single(result => result.role == "client-b");

        if (host.completedCycles < 4 ||
            clientA.completedCycles != host.completedCycles ||
            clientB.completedCycles != host.completedCycles)
        {
            throw new InvalidOperationException(
                "Soak did not complete every fault type on all roles.");
        }

        string[] requiredFaults =
        {
            "MapLoading",
            "Objective",
            "Drag",
            "EnemyAttack"
        };

        for (int i = 0; i < requiredFaults.Length; i++)
        {
            if (host.faults == null ||
                !host.faults.Contains(requiredFaults[i]))
            {
                throw new InvalidOperationException(
                    $"Host did not cover fault '{requiredFaults[i]}'.");
            }
        }

        int mapLoadingCycles = host.faults.Count(
            fault => fault == "MapLoading");
        int expectedInGameReconnects =
            host.completedCycles - mapLoadingCycles;

        if (host.disconnects != host.completedCycles ||
            clientB.disconnects != host.completedCycles ||
            host.reconnects != expectedInGameReconnects ||
            clientB.reconnects != expectedInGameReconnects)
        {
            throw new InvalidOperationException(
                "Disconnect/reconnect counts do not match completed cycles.");
        }

        if (!smoke &&
            host.durationSeconds + 1f < requestedDurationSeconds)
        {
            throw new InvalidOperationException(
                $"Full soak ran for only {host.durationSeconds:F1}s; " +
                $"expected at least {requestedDurationSeconds}s.");
        }
    }

    private static void ThrowIfAnyPlayerFailed(
        IReadOnlyList<PlayerProcess> players,
        string awaitedRole)
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
                RoleResultData result =
                    JsonUtility.FromJson<RoleResultData>(
                        File.ReadAllText(resultPath));

                if (result != null &&
                    result.succeeded &&
                    player.Process.ExitCode == 0)
                {
                    continue;
                }
            }

            throw new InvalidOperationException(
                $"Player '{player.Role}' exited before " +
                $"'{awaitedRole}' completed.\n" +
                ReadLogTail(player.LogPath));
        }
    }

    private static void StopRemainingPlayers(
        IReadOnlyList<PlayerProcess> players)
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
                    $"Could not stop network soak process " +
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
        int requestedDurationSeconds,
        int latencyMs,
        int jitterMs,
        float packetLossPercent,
        IReadOnlyList<RoleResultData> roleResults,
        Exception failure)
    {
        DateTime finishedUtc = DateTime.UtcNow;
        RunSummaryData summary = new()
        {
            succeeded = failure == null,
            message = failure == null
                ? "Host and two clients completed network soak."
                : failure.ToString(),
            startedUtc = startedUtc.ToString("O"),
            finishedUtc = finishedUtc.ToString("O"),
            durationSeconds =
                (float)(finishedUtc - startedUtc).TotalSeconds,
            requestedDurationSeconds = requestedDurationSeconds,
            latencyMs = latencyMs,
            jitterMs = jitterMs,
            packetLossPercent = packetLossPercent,
            roles = roleResults.ToArray()
        };

        try
        {
            WriteAtomic(
                Path.Combine(runDirectory, "summary.json"),
                JsonUtility.ToJson(summary, true));
            WriteNUnitReport(
                Path.Combine(
                    runDirectory,
                    "NetworkSoakResults.xml"),
                summary);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void WriteNUnitReport(
        string path,
        RunSummaryData summary)
    {
        XmlWriterSettings settings = new()
        {
            Indent = true,
            Encoding = new UTF8Encoding(false)
        };

        using XmlWriter writer =
            XmlWriter.Create(path, settings);
        string result =
            summary.succeeded ? "Passed" : "Failed";
        string passed =
            summary.succeeded ? "1" : "0";
        string failed =
            summary.succeeded ? "0" : "1";

        writer.WriteStartDocument();
        writer.WriteStartElement("test-run");
        writer.WriteAttributeString("id", "network-soak");
        writer.WriteAttributeString("testcasecount", "1");
        writer.WriteAttributeString("total", "1");
        writer.WriteAttributeString("passed", passed);
        writer.WriteAttributeString("failed", failed);
        writer.WriteAttributeString("result", result);
        writer.WriteAttributeString(
            "duration",
            summary.durationSeconds.ToString(
                "F3",
                CultureInfo.InvariantCulture));
        writer.WriteStartElement("test-suite");
        writer.WriteAttributeString("type", "Assembly");
        writer.WriteAttributeString("name", "NetworkSoak");
        writer.WriteAttributeString("result", result);
        writer.WriteStartElement("test-case");
        writer.WriteAttributeString(
            "name",
            "HostTwoClientsLatencyLossDisconnectReconnectAndLeakAudit");
        writer.WriteAttributeString(
            "fullname",
            "NetworkSoak.HostTwoClientsLatencyLossDisconnectReconnectAndLeakAudit");
        writer.WriteAttributeString("result", result);
        writer.WriteAttributeString(
            "duration",
            summary.durationSeconds.ToString(
                "F3",
                CultureInfo.InvariantCulture));

        if (!summary.succeeded)
        {
            writer.WriteStartElement("failure");
            writer.WriteElementString(
                "message",
                summary.message);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static int ReadDurationSeconds(bool smoke)
    {
        int fallback =
            smoke ? SmokeDurationSeconds : FullDurationSeconds;
        int minimum = smoke ? 20 : 900;
        return ReadInteger(
            "WIA_NETWORK_SOAK_DURATION_SECONDS",
            fallback,
            minimum,
            1800);
    }

    private static int ReadInteger(
        string variable,
        int fallback,
        int minimum,
        int maximum)
    {
        string value =
            Environment.GetEnvironmentVariable(variable);
        return int.TryParse(
                   value,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out int parsed)
            ? Mathf.Clamp(parsed, minimum, maximum)
            : fallback;
    }

    private static float ReadFloat(
        string variable,
        float fallback,
        float minimum,
        float maximum)
    {
        string value =
            Environment.GetEnvironmentVariable(variable);
        return float.TryParse(
                   value,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out float parsed)
            ? Mathf.Clamp(parsed, minimum, maximum)
            : fallback;
    }

    private static bool ReadBoolean(string variable)
    {
        string value =
            Environment.GetEnvironmentVariable(variable);
        return string.Equals(
                   value,
                   "1",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   value,
                   "true",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveArtifactRoot(
        string projectRoot)
    {
        string configured =
            Environment.GetEnvironmentVariable(
                "WIA_NETWORK_SOAK_ARTIFACTS");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                projectRoot,
                "artifacts",
                "network-soak")
            : Path.GetFullPath(configured);
    }

    private static string QuoteArgument(string value)
    {
        return "\"" +
               value.Replace("\"", "\\\"") +
               "\"";
    }

    private static string ReadLogTail(string path)
    {
        if (!File.Exists(path))
            return $"Log file is missing: {path}";

        string[] lines = File.ReadAllLines(path);
        int start = Math.Max(0, lines.Length - 100);
        return string.Join(
            Environment.NewLine,
            lines.Skip(start));
    }

    private static void WriteAtomic(
        string path,
        string content)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(path) ?? string.Empty);
        string temporaryPath =
            path + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, content);

        if (File.Exists(path))
            File.Delete(path);

        File.Move(temporaryPath, path);
    }

    private sealed class PlayerProcess
    {
        internal PlayerProcess(
            string role,
            string logPath,
            Process process)
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
        public int completedCycles;
        public int disconnects;
        public int reconnects;
        public int maxSpawnedObjects;
        public int maxSceneScopes;
        public string[] faults;
    }

    [Serializable]
    private sealed class RunSummaryData
    {
        public bool succeeded;
        public string message;
        public string startedUtc;
        public string finishedUtc;
        public float durationSeconds;
        public int requestedDurationSeconds;
        public int latencyMs;
        public int jitterMs;
        public float packetLossPercent;
        public RoleResultData[] roles;
    }
}
