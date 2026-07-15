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

internal sealed class SceneScopePauseRegistrationFeature :
    SceneRuntimeFeature,
    IPauseService
{
    public bool IsPaused { get; private set; }

    public event Action<bool> PauseStateChanged;

    protected override bool ValidateFeature(SceneFeatureContext context)
    {
        return context.Services.TryResolve(out ISceneScopeTestParentService _);
    }

    protected override bool InstallFeature(SceneFeatureContext context)
    {
        context.Registrar.Register<IPauseService>(this);
        return true;
    }

    public void Pause()
    {
        SetPaused(true);
    }

    public void Resume()
    {
        SetPaused(false);
    }

    public void TogglePause()
    {
        SetPaused(!IsPaused);
    }

    private void SetPaused(bool paused)
    {
        if (IsPaused == paused)
            return;

        IsPaused = paused;
        PauseStateChanged?.Invoke(paused);
    }
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
    public void ProjectPolicy_DefinesRequiredFeaturesContractsAndParents()
    {
        Assert.That(
            ProjectSceneScopePolicy.TryGetRequirements(
                ProjectSceneKind.Bootstrap,
                false,
                out _),
            Is.False);
        Assert.That(
            ProjectSceneScopePolicy.TryGetRequirements(
                ProjectSceneKind.GameplayTest,
                false,
                out _),
            Is.False);

        GameObject mainMenuObject = new("Main menu feature");
        GameObject lobbyObject = new("Lobby feature");
        GameObject gameObject = new("Game feature");

        try
        {
            MainMenuSceneFeature mainMenu =
                mainMenuObject.AddComponent<MainMenuSceneFeature>();
            LobbySceneFeature lobby = lobbyObject.AddComponent<LobbySceneFeature>();
            GameSceneFeature game = gameObject.AddComponent<GameSceneFeature>();

            Assert.That(
                ProjectSceneScopePolicy.TryGetRequirements(
                    ProjectSceneKind.MainMenu,
                    false,
                    out ProjectSceneScopeRequirements mainMenuRequirements),
                Is.True);
            Assert.That(
                mainMenuRequirements.Parent,
                Is.EqualTo(SceneServiceScopeParent.Global));
            Assert.That(
                mainMenuRequirements.ServicePolicy,
                Is.SameAs(SceneContractPolicy.MainMenu));
            Assert.That(mainMenuRequirements.RequiresSceneRuntime, Is.True);
            Assert.That(
                mainMenuRequirements.ValidateConfiguredFeatures(
                    new SceneRuntimeFeature[] { mainMenu },
                    "Main menu scene"),
                Is.True);

            Assert.That(
                ProjectSceneScopePolicy.TryGetRequirements(
                    ProjectSceneKind.Lobby,
                    false,
                    out ProjectSceneScopeRequirements lobbyRequirements),
                Is.True);
            Assert.That(
                lobbyRequirements.Parent,
                Is.EqualTo(SceneServiceScopeParent.Session));
            Assert.That(
                lobbyRequirements.ServicePolicy,
                Is.SameAs(SceneContractPolicy.Lobby));
            Assert.That(lobbyRequirements.RequiresSceneRuntime, Is.True);
            Assert.That(
                lobbyRequirements.ValidateConfiguredFeatures(
                    new SceneRuntimeFeature[] { lobby },
                    "Lobby scene"),
                Is.True);

            Assert.That(
                ProjectSceneScopePolicy.TryGetRequirements(
                    ProjectSceneKind.Game,
                    false,
                    out ProjectSceneScopeRequirements gameRequirements),
                Is.True);
            Assert.That(
                gameRequirements.Parent,
                Is.EqualTo(SceneServiceScopeParent.Session));
            Assert.That(
                gameRequirements.ServicePolicy,
                Is.SameAs(SceneContractPolicy.Game));
            Assert.That(gameRequirements.RequiresSceneRuntime, Is.True);
            Assert.That(
                gameRequirements.ValidateConfiguredFeatures(
                    new SceneRuntimeFeature[] { game },
                    "Game scene"),
                Is.True);

            Assert.That(
                ProjectSceneScopePolicy.TryGetRequirements(
                    ProjectSceneKind.Unknown,
                    true,
                    out ProjectSceneScopeRequirements mapRequirements),
                Is.True);
            Assert.That(
                mapRequirements.Parent,
                Is.EqualTo(SceneServiceScopeParent.Session));
            Assert.That(
                mapRequirements.ServicePolicy,
                Is.SameAs(SceneContractPolicy.Map));
            Assert.That(mapRequirements.RequiresSceneRuntime, Is.False);
            Assert.That(
                mapRequirements.ValidateConfiguredFeatures(
                    Array.Empty<SceneRuntimeFeature>(),
                    "Map scene"),
                Is.True);

            LogAssert.Expect(
                LogType.Error,
                "Scene 'Lobby scene' (Lobby) requires feature " +
                "'LobbySceneFeature', but it is not configured.");
            Assert.That(
                lobbyRequirements.ValidateConfiguredFeatures(
                    new SceneRuntimeFeature[] { mainMenu },
                    "Lobby scene"),
                Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
            UnityEngine.Object.DestroyImmediate(lobbyObject);
            UnityEngine.Object.DestroyImmediate(mainMenuObject);
        }
    }

    [Test]
    public void RequiredContractValidation_RollsBackBeforeScopeCommit()
    {
        List<string> events = new();
        ServiceScope globalScope = new("Global");
        globalScope.Register<ISceneScopeTestParentService>(new SceneScopeTestParentService());
        GameObject inheritedPauseObject = new("Inherited pause service");
        SceneScopePauseRegistrationFeature inheritedPause =
            inheritedPauseObject.AddComponent<SceneScopePauseRegistrationFeature>();
        globalScope.Register<IPauseService>(inheritedPause);
        ServiceScope sceneServiceScope = globalScope.CreateChild(
            "Scene[95]",
            TestServiceRegistrationPolicy.Instance);
        GameObject featureObject = new("Missing pause registration feature");
        SceneRuntimeScope runtimeScope = null;

        try
        {
            SceneScopeTrackingFeature feature =
                featureObject.AddComponent<SceneScopeTrackingFeature>();
            feature.Configure("missing-pause", events, registersService: true);
            ProjectSceneScopePolicy.TryGetRequirements(
                ProjectSceneKind.Game,
                false,
                out ProjectSceneScopeRequirements requirements);
            runtimeScope = new SceneRuntimeScope(
                95,
                "Missing pause scene",
                ProjectSceneKind.Game,
                SceneServiceScopeParent.Session,
                sceneServiceScope,
                new SceneRuntimeFeature[] { feature },
                scopeReadyValidator: services =>
                    requirements.ValidateReadyServices(services, "Missing pause scene"));

            LogAssert.Expect(
                LogType.Error,
                "Scene scope 'Missing pause scene' is missing required local contract " +
                "'IPauseService'.");
            LogAssert.Expect(
                LogType.Error,
                "Scene scope readiness validation failed for 'Missing pause scene' (95).");

            Assert.That(runtimeScope.Install(), Is.False);

            CollectionAssert.AreEqual(
                new[] { "install:missing-pause", "uninstall:missing-pause" },
                events);
            Assert.That(feature.ResolvedRegistrationDuringUninstall, Is.True);
            Assert.That(sceneServiceScope.IsDisposed, Is.True);
            Assert.That(globalScope.ChildScopeCount, Is.Zero);
        }
        finally
        {
            runtimeScope?.Dispose();
            UnityEngine.Object.DestroyImmediate(featureObject);
            UnityEngine.Object.DestroyImmediate(inheritedPauseObject);
            globalScope.Dispose();
        }
    }

    [Test]
    public void RequiredContractValidation_CommitsOnlyAfterLocalContractExists()
    {
        using ServiceScope globalScope = new("Global");
        globalScope.Register<ISceneScopeTestParentService>(new SceneScopeTestParentService());
        ServiceScope sceneServiceScope = globalScope.CreateChild(
            "Scene[96]",
            TestServiceRegistrationPolicy.Instance);
        GameObject featureObject = new("Pause registration feature");
        SceneRuntimeScope runtimeScope = null;

        try
        {
            SceneScopePauseRegistrationFeature feature =
                featureObject.AddComponent<SceneScopePauseRegistrationFeature>();
            ProjectSceneScopePolicy.TryGetRequirements(
                ProjectSceneKind.Game,
                false,
                out ProjectSceneScopeRequirements requirements);
            runtimeScope = new SceneRuntimeScope(
                96,
                "Ready game scene",
                ProjectSceneKind.Game,
                SceneServiceScopeParent.Session,
                sceneServiceScope,
                new SceneRuntimeFeature[] { feature },
                scopeReadyValidator: services =>
                    requirements.ValidateReadyServices(services, "Ready game scene"));

            Assert.That(runtimeScope.Install(), Is.True);
            Assert.That(runtimeScope.IsReady, Is.True);
            Assert.That(runtimeScope.Services.Resolve<IPauseService>(), Is.SameAs(feature));
        }
        finally
        {
            runtimeScope?.Dispose();
            UnityEngine.Object.DestroyImmediate(featureObject);
        }
    }

    [Test]
    public void EmptyFeatureScope_StillOwnsIndependentServiceScope()
    {
        using ServiceScope sessionScope = new("Session");
        ServiceScope sceneServiceScope = sessionScope.CreateChild(
            "Scene[21]",
            TestServiceRegistrationPolicy.Instance);
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
        ServiceScope sceneServiceScope = globalScope.CreateChild(
            "Scene[32]",
            TestServiceRegistrationPolicy.Instance);
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
        ServiceScope sceneServiceScope = globalScope.CreateChild(
            "Scene[42]",
            TestServiceRegistrationPolicy.Instance);
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
    public void DestroyInstalledFeature_RequestsOwningScopeUninstall()
    {
        List<string> events = new();
        ServiceScope globalScope = new("Global");
        globalScope.Register<ISceneScopeTestParentService>(new SceneScopeTestParentService());
        ServiceScope sceneServiceScope = globalScope.CreateChild(
            "Scene[63]",
            TestServiceRegistrationPolicy.Instance);
        GameObject featureObject = new("Destroying feature");
        SceneRuntimeScope runtimeScope = null;
        int uninstallRequestCount = 0;

        try
        {
            SceneScopeTrackingFeature feature =
                featureObject.AddComponent<SceneScopeTrackingFeature>();
            feature.Configure("destroyed", events);
            runtimeScope = new SceneRuntimeScope(
                63,
                "Destroying scene",
                ProjectSceneKind.Game,
                SceneServiceScopeParent.Session,
                sceneServiceScope,
                new SceneRuntimeFeature[] { feature },
                context =>
                {
                    uninstallRequestCount++;

                    if (runtimeScope == null || !runtimeScope.OwnsContext(context))
                        return false;

                    runtimeScope.Dispose();
                    return true;
                });

            Assert.That(runtimeScope.Install(), Is.True);

            UnityEngine.Object.DestroyImmediate(featureObject);
            featureObject = null;

            Assert.That(uninstallRequestCount, Is.EqualTo(1));
            Assert.That(runtimeScope.IsReady, Is.False);
            Assert.That(runtimeScope.Services, Is.Null);
            Assert.That(globalScope.ChildScopeCount, Is.Zero);
            CollectionAssert.AreEqual(
                new[] { "install:destroyed", "uninstall:destroyed" },
                events);
        }
        finally
        {
            runtimeScope?.Dispose();

            if (featureObject != null)
                UnityEngine.Object.DestroyImmediate(featureObject);

            globalScope.Dispose();
        }
    }

    [Test]
    public void FailedInstall_RollsBackFeaturesBeforeServiceScope()
    {
        List<string> events = new();
        ServiceScope globalScope = new("Global");
        globalScope.Register<ISceneScopeTestParentService>(new SceneScopeTestParentService());
        ServiceScope sceneServiceScope = globalScope.CreateChild(
            "Scene[84]",
            TestServiceRegistrationPolicy.Instance);
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
