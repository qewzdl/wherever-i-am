using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

internal sealed class SettingsServiceComposition : IDisposable
{
    private readonly List<ISettingsServiceConsumer> consumers = new();
    private bool disposed;

    private SettingsServiceComposition()
    {
    }

    public static bool TryCompose(
        Scene scene,
        ISettingsService settingsService,
        out SettingsServiceComposition composition)
    {
        composition = null;

        if (!scene.IsValid() || !scene.isLoaded || settingsService == null)
            return false;

        SettingsServiceComposition candidate = new();

        try
        {
            GameObject[] roots = scene.GetRootGameObjects();

            for (int i = 0; i < roots.Length; i++)
                candidate.Compose(roots[i], settingsService);

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
        ISettingsService settingsService,
        out SettingsServiceComposition composition)
    {
        composition = null;

        if (root == null || settingsService == null)
            return false;

        SettingsServiceComposition candidate = new();

        try
        {
            candidate.Compose(root, settingsService);
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

        for (int i = consumers.Count - 1; i >= 0; i--)
        {
            try
            {
                consumers[i]?.ReleaseSettingsService();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        consumers.Clear();
    }

    private void Compose(GameObject root, ISettingsService settingsService)
    {
        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);

        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is not ISettingsServiceConsumer consumer)
                continue;

            consumer.Construct(settingsService);
            consumers.Add(consumer);
        }
    }
}
