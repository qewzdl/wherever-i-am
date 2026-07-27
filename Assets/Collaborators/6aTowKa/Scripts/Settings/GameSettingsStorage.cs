using System;
using System.IO;
using UnityEngine;

public sealed class GameSettingsStorage
{
    private const string SettingsFolderName = "Settings";
    private const string SettingsFileName = "settings.json";

    private readonly string filePath;
    private readonly string backupPath;

    public string FilePath => filePath;

    public GameSettingsStorage()
        : this(Path.Combine(Application.persistentDataPath, SettingsFolderName, SettingsFileName))
    {
    }

    public GameSettingsStorage(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Settings file path cannot be empty.", nameof(filePath));
        }

        this.filePath = filePath;
        backupPath = filePath + ".bak";
    }

    public GameSettingsData Load(GameSettingsData defaults, int qualityLevelCount)
    {
        if (TryLoadFile(filePath, defaults, qualityLevelCount, out GameSettingsData settings))
        {
            return settings;
        }

        if (TryLoadFile(backupPath, defaults, qualityLevelCount, out settings))
        {
            return settings;
        }

        defaults.Sanitize(qualityLevelCount);
        return defaults;
    }

    public void Save(GameSettingsData settings, int qualityLevelCount)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        settings.Sanitize(qualityLevelCount);

        string directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = filePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonUtility.ToJson(settings, true));

        try
        {
            if (File.Exists(filePath))
            {
                File.Replace(temporaryPath, filePath, backupPath, true);
            }
            else
            {
                File.Move(temporaryPath, filePath);
            }
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

    public static bool TryDeserialize(
        string json,
        GameSettingsData defaults,
        int qualityLevelCount,
        out GameSettingsData settings)
    {
        settings = null;

        if (string.IsNullOrWhiteSpace(json) || defaults == null)
        {
            return false;
        }

        try
        {
            JsonUtility.FromJsonOverwrite(json, defaults);
            defaults.Sanitize(qualityLevelCount);
            settings = defaults;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryLoadFile(
        string path,
        GameSettingsData defaults,
        int qualityLevelCount,
        out GameSettingsData settings)
    {
        settings = null;

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            GameSettingsData candidateDefaults = Clone(defaults);
            return TryDeserialize(
                File.ReadAllText(path),
                candidateDefaults,
                qualityLevelCount,
                out settings);
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

    private static GameSettingsData Clone(GameSettingsData source)
    {
        return JsonUtility.FromJson<GameSettingsData>(JsonUtility.ToJson(source));
    }

    private void ReplaceWithCopy(string temporaryPath)
    {
        if (File.Exists(filePath))
        {
            File.Copy(filePath, backupPath, true);
        }

        File.Copy(temporaryPath, filePath, true);
        File.Delete(temporaryPath);
    }
}
