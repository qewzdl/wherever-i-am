using System;
using System.IO;
using UnityEngine;

internal interface INetworkClientIdentityProvider
{
    string GetOrCreatePlayerId();
}

internal sealed class NetworkClientIdentityProvider : INetworkClientIdentityProvider
{
    internal const string PlayerIdArgument = "-wiaPlayerId";

    private readonly NetworkClientIdentityStorage storage;
    private string cachedPlayerId;

    internal NetworkClientIdentityProvider()
        : this(new NetworkClientIdentityStorage())
    {
    }

    internal NetworkClientIdentityProvider(NetworkClientIdentityStorage storage)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public string GetOrCreatePlayerId()
    {
        if (!string.IsNullOrEmpty(cachedPlayerId))
            return cachedPlayerId;

        if (TryReadCommandLineOverride(out cachedPlayerId))
            return cachedPlayerId;

        try
        {
            cachedPlayerId = storage.LoadOrCreate();
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException)
        {
            cachedPlayerId = Guid.NewGuid().ToString("N");
            Debug.LogWarning(
                $"Could not persist the anonymous network player id. " +
                $"Reconnect across process restarts will be unavailable: " +
                exception.Message);
        }

        return cachedPlayerId;
    }

    private static bool TryReadCommandLineOverride(out string playerId)
    {
        string[] arguments = Environment.GetCommandLineArgs();

        for (int i = 0; i < arguments.Length - 1; i++)
        {
            if (!string.Equals(
                    arguments[i],
                    PlayerIdArgument,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return NetworkConnectionPayloadCodec.TryNormalizePlayerId(
                arguments[i + 1],
                out playerId);
        }

        playerId = string.Empty;
        return false;
    }
}

internal sealed class NetworkClientIdentityStorage
{
    private const int CurrentVersion = 1;
    private const string DirectoryName = "Network";
    private const string FileName = "identity.json";

    private readonly string filePath;
    private readonly string backupPath;

    internal NetworkClientIdentityStorage()
        : this(Path.Combine(
            Application.persistentDataPath,
            DirectoryName,
            FileName))
    {
    }

    internal NetworkClientIdentityStorage(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException(
                "Identity file path cannot be empty.",
                nameof(filePath));
        }

        this.filePath = filePath;
        backupPath = filePath + ".bak";
    }

    internal string LoadOrCreate()
    {
        if (TryLoad(filePath, out string playerId) ||
            TryLoad(backupPath, out playerId))
        {
            return playerId;
        }

        playerId = Guid.NewGuid().ToString("N");
        Save(playerId);
        return playerId;
    }

    private static bool TryLoad(string path, out string playerId)
    {
        playerId = string.Empty;

        if (!File.Exists(path))
            return false;

        try
        {
            IdentityData data = JsonUtility.FromJson<IdentityData>(
                File.ReadAllText(path));

            return data != null &&
                   data.version == CurrentVersion &&
                   NetworkConnectionPayloadCodec.TryNormalizePlayerId(
                       data.playerId,
                       out playerId);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Save(string playerId)
    {
        string directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        IdentityData data = new()
        {
            version = CurrentVersion,
            playerId = playerId
        };
        string temporaryPath = filePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonUtility.ToJson(data, true));

        try
        {
            if (File.Exists(filePath))
                File.Replace(temporaryPath, filePath, backupPath, true);
            else
                File.Move(temporaryPath, filePath);
        }
        catch (PlatformNotSupportedException)
        {
            ReplaceWithCopy(temporaryPath);
        }
        catch (IOException)
        {
            ReplaceWithCopy(temporaryPath);
        }
    }

    private void ReplaceWithCopy(string temporaryPath)
    {
        if (File.Exists(filePath))
            File.Copy(filePath, backupPath, true);

        File.Copy(temporaryPath, filePath, true);
        File.Delete(temporaryPath);
    }

    [Serializable]
    private sealed class IdentityData
    {
        public int version;
        public string playerId;
    }
}
