using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

internal sealed class AudioServiceComposition : IDisposable
{
    private readonly List<IUiSoundServiceConsumer> uiConsumers = new();
    private readonly List<IMusicServiceConsumer> musicConsumers = new();
    private readonly List<IGameplaySoundServiceConsumer> gameplayConsumers = new();
    private bool disposed;

    private AudioServiceComposition()
    {
    }

    public static bool TryCompose(
        Scene scene,
        IAudioService audioService,
        out AudioServiceComposition composition)
    {
        composition = null;

        if (!scene.IsValid() || !scene.isLoaded || audioService == null)
            return false;

        AudioServiceComposition candidate = new();

        try
        {
            GameObject[] roots = scene.GetRootGameObjects();

            for (int i = 0; i < roots.Length; i++)
                candidate.Compose(roots[i], audioService);

            composition = candidate;
            return true;
        }
        catch (Exception exception)
        {
            candidate.Dispose();
            Debug.LogException(exception);
            return false;
        }
    }

    public static bool TryCompose(
        GameObject root,
        IAudioService audioService,
        out AudioServiceComposition composition)
    {
        composition = null;

        if (root == null || audioService == null)
            return false;

        AudioServiceComposition candidate = new();

        try
        {
            candidate.Compose(root, audioService);
            composition = candidate;
            return true;
        }
        catch (Exception exception)
        {
            candidate.Dispose();
            Debug.LogException(exception, root);
            return false;
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        for (int i = gameplayConsumers.Count - 1; i >= 0; i--)
            TryRelease(gameplayConsumers[i].ReleaseGameplaySoundService);

        for (int i = musicConsumers.Count - 1; i >= 0; i--)
            TryRelease(musicConsumers[i].ReleaseMusicService);

        for (int i = uiConsumers.Count - 1; i >= 0; i--)
            TryRelease(uiConsumers[i].ReleaseUiSoundService);

        gameplayConsumers.Clear();
        musicConsumers.Clear();
        uiConsumers.Clear();
    }

    private void Compose(GameObject root, IAudioService audioService)
    {
        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);

        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];

            if (behaviour is IUiSoundServiceConsumer uiConsumer)
            {
                uiConsumer.Construct(audioService.UI);
                uiConsumers.Add(uiConsumer);
            }

            if (behaviour is IMusicServiceConsumer musicConsumer)
            {
                musicConsumer.Construct(audioService.Music);
                musicConsumers.Add(musicConsumer);
            }

            if (behaviour is IGameplaySoundServiceConsumer gameplayConsumer)
            {
                gameplayConsumer.Construct(audioService.Gameplay);
                gameplayConsumers.Add(gameplayConsumer);
            }
        }
    }

    private static void TryRelease(Action release)
    {
        try
        {
            release?.Invoke();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }
}
