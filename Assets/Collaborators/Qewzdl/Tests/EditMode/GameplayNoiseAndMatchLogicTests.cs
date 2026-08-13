using NUnit.Framework;
using UnityEngine;

[Category("Gameplay")]
public sealed class GameplayNoiseAndMatchLogicTests
{
    [Test]
    public void NoiseEvent_ClampsInvalidValuesAndTracksSourceIdentity()
    {
        GameplayNoiseEvent invalid = new(
            Vector3.one,
            -4f,
            -2f,
            10f,
            GameplayNoiseSourceType.Unknown,
            GameplayNoiseEvent.NoNetworkObjectId,
            GameplayNoiseEvent.NoClientId,
            null);

        Assert.That(invalid.Radius, Is.Zero);
        Assert.That(invalid.Loudness, Is.Zero);
        Assert.That(invalid.IsValid, Is.False);
        Assert.That(invalid.HasNetworkSource, Is.False);
        Assert.That(invalid.HasClientSource, Is.False);

        GameplayNoiseEvent valid = new(
            new Vector3(2f, 0f, 3f),
            8f,
            0.75f,
            12f,
            GameplayNoiseSourceType.Player,
            42,
            7,
            null);

        Assert.That(valid.IsValid, Is.True);
        Assert.That(valid.HasNetworkSource, Is.True);
        Assert.That(valid.HasClientSource, Is.True);
        Assert.That(valid.SourceNetworkObjectId, Is.EqualTo(42));
        Assert.That(valid.SourceClientId, Is.EqualTo(7));
    }

    [Test]
    public void NoiseQuery_NormalizesEveryBoundary()
    {
        GameplayNoiseQuery invalid = new(-1f, -2f, -3f);

        Assert.That(invalid.HearingRadius, Is.Zero);
        Assert.That(invalid.MemoryDuration, Is.Zero);
        Assert.That(invalid.MinimumLoudness, Is.Zero);
        Assert.That(invalid.IsValid, Is.False);

        GameplayNoiseQuery valid = new(4f, 0f, 0.2f);
        Assert.That(valid.IsValid, Is.True);
    }

    [Test]
    public void NoisePreset_IsValidOnlyForAudibleKnownSource()
    {
        GameplayNoisePreset preset = ScriptableObject.CreateInstance<GameplayNoisePreset>();

        try
        {
            TestReflection.SetField(preset, "sourceType", GameplayNoiseSourceType.Unknown);
            TestReflection.SetField(preset, "radius", -5f);
            TestReflection.SetField(preset, "loudness", -1f);
            TestReflection.SetField(preset, "serverCooldown", -2f);

            Assert.That(preset.Radius, Is.Zero);
            Assert.That(preset.Loudness, Is.Zero);
            Assert.That(preset.ServerCooldown, Is.Zero);
            Assert.That(preset.IsValid, Is.False);

            TestReflection.SetField(preset, "sourceType", GameplayNoiseSourceType.Item);
            TestReflection.SetField(preset, "radius", 6f);
            TestReflection.SetField(preset, "loudness", 0.5f);

            Assert.That(preset.IsValid, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(preset);
        }
    }

    [Test]
    public void ItemImpactNoiseProfile_MapsEverySupportedImpact()
    {
        GameplayNoisePreset light = CreateValidPreset(GameplayNoiseSourceType.Item);
        GameplayNoisePreset medium = CreateValidPreset(GameplayNoiseSourceType.Item);
        GameplayNoisePreset heavy = CreateValidPreset(GameplayNoiseSourceType.Item);
        GameplayNoisePreset landing = CreateValidPreset(GameplayNoiseSourceType.Player);
        ItemImpactNoiseProfile profile =
            ScriptableObject.CreateInstance<ItemImpactNoiseProfile>();

        try
        {
            TestReflection.SetField(profile, "lightImpactNoise", light);
            TestReflection.SetField(profile, "mediumImpactNoise", medium);
            TestReflection.SetField(profile, "heavyImpactNoise", heavy);
            TestReflection.SetField(profile, "landingNoise", landing);

            Assert.That(profile.HasAnyNoise, Is.True);
            AssertPreset(profile, ItemImpactSoundId.LightImpact, light);
            AssertPreset(profile, ItemImpactSoundId.MediumImpact, medium);
            AssertPreset(profile, ItemImpactSoundId.HeavyImpact, heavy);
            AssertPreset(profile, ItemImpactSoundId.Landing, landing);
            Assert.That(profile.TryGetPreset(ItemImpactSoundId.None, out _), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(light);
            Object.DestroyImmediate(medium);
            Object.DestroyImmediate(heavy);
            Object.DestroyImmediate(landing);
        }
    }

    [TestCase(GameResultType.None, MatchResultSource.Objective, "exit", false)]
    [TestCase(GameResultType.Victory, MatchResultSource.None, "exit", false)]
    [TestCase(GameResultType.Victory, MatchResultSource.Objective, "", false)]
    [TestCase(GameResultType.Victory, MatchResultSource.Objective, "exit", true)]
    [TestCase(GameResultType.Defeat, MatchResultSource.PlayerCaught, "", true)]
    public void GameResultValidation_EnforcesSourceSpecificIdentity(
        GameResultType result,
        MatchResultSource source,
        string sourceId,
        bool expected)
    {
        Assert.That(
            GameResultData.IsValidResult(result, source, sourceId, 10),
            Is.EqualTo(expected));
    }

    [Test]
    public void MatchOutcomeFactory_RejectsNoneAndPreservesResultData()
    {
        MatchOutcome caught = MatchOutcomeFactory.FromPlayerCaught(
            GameResultType.Defeat,
            5,
            "A player was caught by an enemy");

        Assert.That(caught.HasResult, Is.True);
        Assert.That(caught.Source, Is.EqualTo(MatchResultSource.PlayerCaught));
        Assert.That(caught.SourceId, Is.EqualTo("player_caught"));
        Assert.That(caught.InstigatorClientId, Is.EqualTo(5));

        Assert.That(
            MatchOutcomeFactory
                .FromPlayerCaught(GameResultType.None, 5, "ignored")
                .HasResult,
            Is.False);

        GameResultData data = MatchOutcomeFactory
            .FromPlayerCaught(GameResultType.Defeat, 17, null)
            .ToGameResultData();

        Assert.That(data.HasResult, Is.True);
        Assert.That(data.ResultType, Is.EqualTo(GameResultType.Defeat));
        Assert.That(data.SourceId.ToString(), Is.EqualTo("player_caught"));
        Assert.That(data.Reason.ToString(), Is.Empty);
        Assert.That(data.InstigatorClientId, Is.EqualTo(17));
    }

    private static GameplayNoisePreset CreateValidPreset(
        GameplayNoiseSourceType sourceType)
    {
        GameplayNoisePreset preset = ScriptableObject.CreateInstance<GameplayNoisePreset>();
        TestReflection.SetField(preset, "sourceType", sourceType);
        TestReflection.SetField(preset, "radius", 5f);
        TestReflection.SetField(preset, "loudness", 1f);
        return preset;
    }

    private static void AssertPreset(
        ItemImpactNoiseProfile profile,
        ItemImpactSoundId impact,
        GameplayNoisePreset expected)
    {
        Assert.That(profile.TryGetPreset(impact, out GameplayNoisePreset actual), Is.True);
        Assert.That(actual, Is.SameAs(expected));
    }

    [Test]
    public void NoiseScore_FadesWithAgeSoAFreshSoundCanOutrankAnOldLouderOne()
    {
        const float memoryDuration = 3f;

        float freshLoud = GameplayNoiseWorldService.ScoreNoise(
            loudness: 10f,
            distance: 0f,
            effectiveRadius: 10f,
            age: 0f,
            memoryDuration: memoryDuration);

        float staleLoud = GameplayNoiseWorldService.ScoreNoise(
            loudness: 10f,
            distance: 0f,
            effectiveRadius: 10f,
            age: 2.7f,
            memoryDuration: memoryDuration);

        float freshQuiet = GameplayNoiseWorldService.ScoreNoise(
            loudness: 3f,
            distance: 0f,
            effectiveRadius: 10f,
            age: 0f,
            memoryDuration: memoryDuration);

        Assert.That(staleLoud, Is.LessThan(freshLoud));
        Assert.That(
            freshQuiet,
            Is.GreaterThan(staleLoud),
            "An almost-forgotten bang should not outrank a new sound nearby.");

        // No cliff at the memory horizon: the last remembered frame is worth
        // nothing, rather than dropping from full value to gone.
        Assert.That(
            GameplayNoiseWorldService.ScoreNoise(
                loudness: 10f,
                distance: 0f,
                effectiveRadius: 10f,
                age: memoryDuration,
                memoryDuration: memoryDuration),
            Is.EqualTo(0f).Within(0.0001f));
    }
}
