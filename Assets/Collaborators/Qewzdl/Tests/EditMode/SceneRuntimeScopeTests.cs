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

internal interface ISceneScopeTestRegisteredService
{
}

internal sealed class SceneScopeTestParentService : ISceneScopeTestParentService
{
}

internal sealed class SceneScopeTestOwnedService : ISceneScopeTestOwnedService
{
}

internal sealed class SceneScopeTrackingFeature :
    SceneRuntimeFeature,
    ISceneScopeTestRegisteredService
{
    private IList<string> events;
    private string featureId;
    private bool installResult;
    private bool registerService;

    public bool ResolvedParentDuringValidation { get; private set; }
    public bool ResolvedParentDuringUninstall { get; private set; }
    public bool ResolvedRegistrationDuringInstall { get; private set; }
    public bool ResolvedRegistrationDuringUninstall { get; private set; }
    public ISceneServiceRegistrar Registrar { get; private set; }

    public void Configure(
        string id,
        IList<string> lifecycleEvents,
        bool succeeds = true,
        bool registersService = false)
    {
        featureId = id;
        events = lifecycleEvents;
        installResult = succeeds;
        registerService = registersService;
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

        if (registerService)
        {
            Registrar = context.Registrar;
            Registrar.Register<ISceneScopeTestRegisteredService>(this);
            ResolvedRegistrationDuringInstall =
                ReferenceEquals(
                    context.Services.Resolve<ISceneScopeTestRegisteredService>(),
                    this);
        }

        return installResult;
    }

    protected override void UninstallFeature(SceneFeatureContext context)
    {
        ResolvedParentDuringUninstall =
            context.Services.TryResolve(out ISceneScopeTestParentService _);

        if (registerService)
        {
            ResolvedRegistrationDuringUninstall =
                ReferenceEquals(
                    context.Services.Resolve<ISceneScopeTestRegisteredService>(),
                    this);
        }

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
    public void Install_CommitsFeatureRegistrationAndClosesRegistrar()
    {
        using ServiceScope globalScope = new("Global");
        globalScope.Register<ISceneScopeTestParentService>(new SceneScopeTestParentService());
        ServiceScope sceneServiceScope = globalScope.CreateChild("Scene[32]");
        GameObject featureObject = new("Registering feature");
        SceneRuntimeScope runtimeScope = null;

        try
        {
            List<string> events = new();
            SceneScopeTrackingFeature feature =
                featureObject.AddComponent<SceneScopeTrackingFeature>();
            feature.Configure("registrar", events, registersService: true);
            runtimeScope = new SceneRuntimeScope(
                32,
                "Registration scene",
                ProjectSceneKind.Game,
                SceneServiceScopeParent.Session,
                sceneServiceScope,
                new SceneRuntimeFeature[] { feature });

            Assert.That(runtimeScope.Services, Is.Null);
            Assert.That(runtimeScope.Install(), Is.True);
            Assert.That(feature.ResolvedRegistrationDuringInstall, Is.True);
            Assert.That(
                runtimeScope.Services.Resolve<ISceneScopeTestRegisteredService>(),
                Is.SameAs(feature));
            Assert.That(sceneServiceScope.LocalServiceCount, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() =>
                feature.Registrar.Register<ISceneScopeTestRegisteredService>(feature));

            runtimeScope.Dispose();

            Assert.That(feature.ResolvedRegistrationDuringUninstall, Is.True);
            CollectionAssert.AreEqual(
                new[] { "install:registrar", "uninstall:registrar" },
                events);
            Assert.That(sceneServiceScope.LocalServiceCount, Is.Zero);
        }
        finally
        {
            runtimeScope?.Dispose();
            UnityEngine.Object.DestroyImmediate(featureObject);
        }
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
            first.Configure("first", events, registersService: true);
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
            Assert.That(first.ResolvedRegistrationDuringInstall, Is.True);
            Assert.That(first.ResolvedRegistrationDuringUninstall, Is.True);
            Assert.That(second.ResolvedParentDuringUninstall, Is.True);
            Assert.That(runtimeScope.Services, Is.Null);
            Assert.That(sceneServiceScope.LocalServiceCount, Is.Zero);
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
            first.Configure("first", events, registersService: true);
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
            Assert.That(first.ResolvedRegistrationDuringInstall, Is.True);
            Assert.That(first.ResolvedRegistrationDuringUninstall, Is.True);
            Assert.That(failing.ResolvedParentDuringUninstall, Is.True);
            Assert.That(sceneServiceScope.LocalServiceCount, Is.Zero);
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
