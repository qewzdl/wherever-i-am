using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyHearingSceneComposition : MonoBehaviour
{
    [Header("Service")]
    [SerializeField] private EnemyNoiseWorldService noiseWorldService;

    [Header("Receivers")]
    [SerializeField] private EnemyHearingSensor[] hearingSensors;

    [Header("Emitters")]
    [SerializeField] private EnemyNoiseEmitter[] noiseEmitters;

    private bool composed;

    private void Awake()
    {
        Compose();
    }

    public void Compose()
    {
        if (composed)
        {
            return;
        }

        if (noiseWorldService == null)
        {
            Debug.LogError(
                $"{nameof(EnemyHearingSceneComposition)} requires {nameof(EnemyNoiseWorldService)}.",
                this
            );

            return;
        }

        noiseWorldService.Initialize();

        ComposeHearingSensors();
        ComposeNoiseEmitters();

        composed = true;
    }

    private void ComposeHearingSensors()
    {
        if (hearingSensors == null)
        {
            return;
        }

        for (int i = 0; i < hearingSensors.Length; i++)
        {
            EnemyHearingSensor hearingSensor = hearingSensors[i];

            if (hearingSensor == null)
            {
                Debug.LogError(
                    $"{nameof(EnemyHearingSceneComposition)} has missing {nameof(EnemyHearingSensor)} reference at index {i}.",
                    this
                );

                continue;
            }

            hearingSensor.Construct(noiseWorldService);
        }
    }

    private void ComposeNoiseEmitters()
    {
        if (noiseEmitters == null)
        {
            return;
        }

        for (int i = 0; i < noiseEmitters.Length; i++)
        {
            EnemyNoiseEmitter noiseEmitter = noiseEmitters[i];

            if (noiseEmitter == null)
            {
                Debug.LogError(
                    $"{nameof(EnemyHearingSceneComposition)} has missing {nameof(EnemyNoiseEmitter)} reference at index {i}.",
                    this
                );

                continue;
            }

            noiseEmitter.Construct(noiseWorldService);
        }
    }
}