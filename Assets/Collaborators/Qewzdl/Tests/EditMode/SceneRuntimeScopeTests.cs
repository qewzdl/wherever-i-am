using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

internal interface ISceneScopeTestParentService
{
}

internal interface ISceneScopeTestOwnedService
{
}

internal sealed class SceneScopeTestParentService : ISceneScopeTestParentService
{
}

internal sealed class SceneScopeTestOwnedService : ISceneScopeTestOwnedService
{
}

internal sealed class SceneScopeTrackingFeature : SceneRuntimeFeature
{
    private IList<string> events;
    private string featureId;
    private bool installResult;

    public bool ResolvedParentDuringValidation { get; private set; }
    public bool ResolvedParentDuringUninstall { get; private set; }

    public void Configure(string id, IList<string> lifecycleEvents, bool succeeds = true)
    {
        featureId = id;
        events = lifecycleEvents;
        installResult = succeeds;
    }

    protected override bool ValidateFeature(SceneFeatureContext context)
    {
        ResolvedParentDuringValidation =
            context.Services.TryResolve(out ISceneScopeTestParentService _);

        return ResolvedParentDuringValidation;
    }

    protected override bool InstallFeature(SceneFeatureContext context)
    {
        events.Add($"install:{featureId}");
        return installResult;
    }

    protected override void UninstallFeature(SceneFeatureContext context)
    {
        ResolvedParentDuringUninstall =
            context.Services.TryResolve(out ISceneScopeTestParentService _);
        events.Add($"uninstall:{featureId}");
    }
}

public sealed class SceneRuntimeScopeTests
{
    [Test]
    public void EmptyFeatureScope_StillOwnsIndependentServiceScope()
    {
        using ServiceScope sessionScope = new("Session");
        ServiceScope sceneServiceScope = sessionScope.CreateChild("Scene[21]");
        SceneRuntimeScope runtimeScope = new(
            21,
            "Map scene",
            ProjectSceneKind.Unknown,
            SceneServiceScopeParent.Session,
            sceneServiceScope,
            Array.Empty<SceneRuntimeFeature>());

        Assert.That(runtimeScope.Install(), Is.True);
        Assert.That(runtimeScope.IsReady, Is.True);
        Assert.That(runtimeScope.Services, Is.SameAs(sceneServiceScope));
        Assert.That(sessionScope.ChildScopeCount, Is.EqualTo(1));

        runtimeScope.Dispose();

        Assert.That(runtimeScope.Services, Is.Null);
        Assert.That(sessionScope.ChildScopeCount, Is.Zero);
    }

    [Test]
    public void Dispose_UninstallsFeaturesInReverseBeforeServiceScope()
    {
        List<string> events = new();
        ServiceScope globalScope = new("Global");
        globalScope.Register<ISceneScopeTestParentService>(new SceneScopeTestParentService());
        ServiceScope sceneServiceScope = globalScope.CreateChild("Scene[42]");
        sceneServiceScope.Register<ISceneScopeTestOwnedService>(
            new SceneScopeTestOwnedService(),
            ServiceRegistrationOwnership.ScopeOwned,
            cleanup: _ => events.Add("dispose:scope"));
        GameObject firstObject = new("First feature");
        GameObject secondObject = new("Second feature");
        SceneRuntimeScope runtimeScope = null;

        try
        {
            SceneScopeTrackingFeature first = firstObject.AddComponent<SceneScopeTrackingFeature>();
            SceneScopeTrackingFeature second = secondObject.AddComponent<SceneScopeTrackingFeature>();
            first.Configure("first", events);
            second.Configure("second", events);
            runtimeScope = new SceneRuntimeScope(
                42,
                "Test scene",
                ProjectSceneKind.Game,
                SceneServiceScopeParent.Session,
                sceneServiceScope,
                new SceneRuntimeFeature[] { first, second });

            Assert.That(runtimeScope.Install(), Is.True);
            Assert.That(first.ResolvedParentDuringValidation, Is.True);
            Assert.That(second.ResolvedParentDuringValidation, Is.True);
            IServiceResolver resolver = runtimeScope.Services;

            runtimeScope.Dispose();
            runtimeScope.Dispose();

            CollectionAssert.AreEqual(
                new[]
                {
                    "install:first",
                    "install:second",
                    "uninstall:second",
                    "uninstall:first",
                    "dispose:scope"
                },
                events);
            Assert.That(first.ResolvedParentDuringUninstall, Is.True);
            Assert.That(second.ResolvedParentDuringUninstall, Is.True);
            Assert.That(runtimeScope.Services, Is.Null);
            Assert.That(globalScope.ChildScopeCount, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() =>
                resolver.Resolve<ISceneScopeTestParentService>());
        }
        finally
        {
            runtimeScope?.Dispose();
            UnityEngine.Object.DestroyImmediate(secondObject);
            UnityEngine.Object.DestroyImmediate(firstObject);
            globalScope.Dispose();
        }
    }

    [Test]
    public void FailedInstall_RollsBackFeaturesBeforeServiceScope()
    {
        List<string> events = new();
        ServiceScope globalScope = new("Global");
        globalScope.Register<ISceneScopeTestParentService>(new SceneScopeTestParentService());
        ServiceScope sceneServiceScope = globalScope.CreateChild("Scene[84]");
        sceneServiceScope.Register<ISceneScopeTestOwnedService>(
            new SceneScopeTestOwnedService(),
            ServiceRegistrationOwnership.ScopeOwned,
            cleanup: _ => events.Add("dispose:scope"));
        GameObject firstObject = new("First feature");
        GameObject failingObject = new("Failing feature");
        SceneRuntimeScope runtimeScope = null;

        try
        {
            SceneScopeTrackingFeature first = firstObject.AddComponent<SceneScopeTrackingFeature>();
            SceneScopeTrackingFeature failing = failingObject.AddComponent<SceneScopeTrackingFeature>();
            first.Configure("first", events);
            failing.Configure("failing", events, false);
            runtimeScope = new SceneRuntimeScope(
                84,
                "Failing scene",
                ProjectSceneKind.Game,
                SceneServiceScopeParent.Session,
                sceneServiceScope,
                new SceneRuntimeFeature[] { first, failing });

            LogAssert.Expect(
                LogType.Error,
                "Scene feature install failed: 'SceneScopeTrackingFeature' in 'Failing scene' (84).");
            Assert.That(runtimeScope.Install(), Is.False);
            runtimeScope.Dispose();

            CollectionAssert.AreEqual(
                new[]
                {
                    "install:first",
                    "install:failing",
                    "uninstall:failing",
                    "uninstall:first",
                    "dispose:scope"
                },
                events);
            Assert.That(first.ResolvedParentDuringUninstall, Is.True);
            Assert.That(failing.ResolvedParentDuringUninstall, Is.True);
            Assert.That(globalScope.ChildScopeCount, Is.Zero);
        }
        finally
        {
            runtimeScope?.Dispose();
            UnityEngine.Object.DestroyImmediate(failingObject);
            UnityEngine.Object.DestroyImmediate(firstObject);
            globalScope.Dispose();
        }
    }
}
