using System;

// What a winded chest does next, with the clock left out of it.
//
// The component that uses this owns the gaps - how long after an inhale the
// exhale comes, how long the pause between breaths is - because those are
// tuning. What is here is the order, and the order carries rules that are
// decisions rather than arithmetic:
//
//   A cough only ever follows an exhale. You cannot cough on the way in; the
//   air has to be going out for there to be a cough at all.
//
//   Never a third exhale, never a third cough. Two is a chest not finishing in
//   one go, which is what being out of breath looks like. Three is a
//   condition, and the player has not got one - they have been running.
//
//   A cough is refused outright until the effort has cost enough. That gate is
//   the caller's to decide; this only honours it.
//
// Pulled out so those four can be asserted. They are rare events by design, so
// losing one in a refactor is close to impossible to hear, and a rule nobody
// can hear break is a rule that wants writing down.
public sealed class BreathRhythm
{
    public enum Step
    {
        Inhale,
        Exhale,
        Cough,
    }

    public readonly struct Chances
    {
        public readonly float DoubleExhale;
        public readonly float Cough;
        public readonly float DoubleCough;

        public Chances(float doubleExhale, float cough, float doubleCough)
        {
            DoubleExhale = doubleExhale;
            Cough = cough;
            DoubleCough = doubleCough;
        }
    }

    private bool lastExhaleWasSecond;
    private bool lastCoughWasSecond;

    public Step Current { get; private set; } = Step.Inhale;

    // True when the step just taken repeats the one before it. The caller uses
    // it to pick the gap: a second exhale sits further from the first than an
    // exhale sits from its inhale, and two coughs sit closer together than
    // anything else.
    public bool IsRepeat { get; private set; }

    public void Reset()
    {
        Current = Step.Inhale;
        IsRepeat = false;
        lastExhaleWasSecond = false;
        lastCoughWasSecond = false;
    }

    // The roll is supplied rather than taken, so that this can be asked what it
    // would do rather than only watched doing it.
    public void Advance(Chances chances, bool coughAllowed, Func<float> roll)
    {
        switch (Current)
        {
            case Step.Inhale:
                Current = Step.Exhale;
                IsRepeat = false;
                lastExhaleWasSecond = false;
                return;

            case Step.Cough:
                AdvanceAfterCough(chances, roll);
                return;

            default:
                AdvanceAfterExhale(chances, coughAllowed, roll);
                return;
        }
    }

    private void AdvanceAfterCough(Chances chances, Func<float> roll)
    {
        if (!lastCoughWasSecond && Roll(roll, chances.DoubleCough))
        {
            Current = Step.Cough;
            IsRepeat = true;
            lastCoughWasSecond = true;
            return;
        }

        Current = Step.Inhale;
        IsRepeat = false;
        lastCoughWasSecond = false;
    }

    private void AdvanceAfterExhale(Chances chances, bool coughAllowed, Func<float> roll)
    {
        // A cough grows out of the first exhale of a breath. Refusing it after
        // a second exhale is what stops a fit assembling itself out of an
        // unlucky run of rolls, and is why the chance can be set high enough
        // to actually hear.
        if (coughAllowed && !lastExhaleWasSecond && Roll(roll, chances.Cough))
        {
            Current = Step.Cough;
            IsRepeat = false;
            lastCoughWasSecond = false;
            return;
        }

        if (!lastExhaleWasSecond && Roll(roll, chances.DoubleExhale))
        {
            Current = Step.Exhale;
            IsRepeat = true;
            lastExhaleWasSecond = true;
            return;
        }

        Current = Step.Inhale;
        IsRepeat = false;
        lastExhaleWasSecond = false;
    }

    private static bool Roll(Func<float> roll, float chance)
    {
        return chance > 0f && roll() < chance;
    }
}
