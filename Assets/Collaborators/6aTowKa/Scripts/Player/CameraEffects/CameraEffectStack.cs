using System.Collections.Generic;

// Plain C# aggregator — the composer half of the contract. Holds the registered effects and
// sums their contributions into one CameraEffectOutput. Deliberately has no UnityEngine
// dependency beyond the structs it passes through, so it can be covered by EditMode tests
// without a scene.
public sealed class CameraEffectStack
{
    private readonly List<ICameraEffect> effects = new();

    public IReadOnlyList<ICameraEffect> Effects => effects;

    public void Add(ICameraEffect effect)
    {
        if (effect == null || effects.Contains(effect))
            return;

        effects.Add(effect);
    }

    public void Remove(ICameraEffect effect)
    {
        if (effect == null)
            return;

        effects.Remove(effect);
    }

    public void Clear()
    {
        effects.Clear();
    }

    public void ResetAll()
    {
        for (int i = 0; i < effects.Count; i++)
            effects[i].Reset();
    }

    public CameraEffectOutput Evaluate(in CameraEffectContext context)
    {
        CameraEffectOutput output = default;
        output.Clear();

        for (int i = 0; i < effects.Count; i++)
        {
            if (!effects[i].Enabled)
                continue;

            // Crouching and hiding each scale an effect by its own multiplier. Folding them
            // into Weight keeps the whole thing in one place: effects already respect Weight,
            // so none of them need to know either state exists. Crouching inside a hiding spot
            // applies both.
            if (context.IsCrouching || context.IsHiding)
            {
                float multiplier = 1f;

                if (context.IsCrouching)
                    multiplier *= effects[i].CrouchMultiplier;

                if (context.IsHiding)
                    multiplier *= effects[i].HidingMultiplier;

                CameraEffectContext scaledContext = context.WithWeight(context.Weight * multiplier);
                effects[i].Evaluate(in scaledContext, ref output);
            }
            else
            {
                effects[i].Evaluate(in context, ref output);
            }
        }

        return output;
    }
}
