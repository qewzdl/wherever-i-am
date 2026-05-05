using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public static class EnemyNoiseSystem
{
    private const int MaxStoredNoises = 128;

    private static readonly List<EnemyNoiseEvent> noises = new();

    public static bool TryRaiseNoiseServer(
        Vector3 position,
        float radius,
        float loudness = 1f,
        EnemyTarget sourceTarget = null,
        Object sourceObject = null
    )
    {
        if (!CanRaiseNoiseServer())
        {
            return false;
        }

        EnemyNoiseEvent noiseEvent = new EnemyNoiseEvent(
            position,
            radius,
            loudness,
            Time.time,
            sourceTarget,
            sourceObject
        );

        if (!noiseEvent.IsValid)
        {
            return false;
        }

        if (noises.Count >= MaxStoredNoises)
        {
            noises.RemoveAt(0);
        }

        noises.Add(noiseEvent);
        return true;
    }

    public static bool TryFindBestNoise(
        Vector3 listenerPosition,
        EnemyConfig config,
        out EnemyPerceptionStimulus stimulus
    )
    {
        stimulus = EnemyPerceptionStimulus.None;

        if (config == null || !config.hearingEnabled)
        {
            return false;
        }

        float now = Time.time;
        float bestScore = 0f;
        EnemyNoiseEvent bestNoise = default;
        bool hasBestNoise = false;

        for (int i = noises.Count - 1; i >= 0; i--)
        {
            EnemyNoiseEvent noise = noises[i];

            if (now - noise.CreatedAtTime > config.hearingMemoryDuration)
            {
                noises.RemoveAt(i);
                continue;
            }

            if (!noise.IsValid || noise.Loudness < config.minimumNoiseLoudness)
            {
                continue;
            }

            float distance = Vector3.Distance(listenerPosition, noise.Position);
            float effectiveRadius = Mathf.Min(config.hearingRadius, noise.Radius);

            if (distance > effectiveRadius)
            {
                continue;
            }

            float normalizedDistance = distance / Mathf.Max(0.001f, effectiveRadius);
            float score = noise.Loudness * (1f - normalizedDistance);

            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestNoise = noise;
            hasBestNoise = true;
        }

        if (!hasBestNoise)
        {
            return false;
        }

        stimulus = EnemyPerceptionStimulus.ForSuspiciousPosition(
            bestNoise.Position,
            bestScore,
            EnemyPerceptionSource.Hearing
        );

        return true;
    }

    public static void Clear()
    {
        noises.Clear();
    }

    public static bool CanRaiseNoiseServer()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        return networkManager != null &&
               networkManager.IsListening &&
               networkManager.IsServer;
    }
}