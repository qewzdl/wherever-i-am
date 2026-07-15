using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class BootstrapGPlayModeTests
{
    private const string BootstrapScenePath =
        "Assets/Collaborators/Qewzdl/Scenes/Bootstrap.unity";
    private const int StartupFrameLimit = 300;

    private Scene persistentScene;
    private GameObject persistentSceneProbe;
    private ProjectContext runtimeContext;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        persistentSceneProbe = new GameObject("Bootstrap G PlayMode Test Probe");
        UnityEngine.Object.DontDestroyOnLoad(persistentSceneProbe);
        persistentScene = persistentSceneProbe.scene;

        yield return DestroyProjectRuntimeRoots();

        G.ResetRuntimeState();
        runtimeContext = null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        yield return DestroyProjectRuntimeRoots();

        G.ResetRuntimeState();
        runtimeContext = null;

        if (persistentSceneProbe != null)
            UnityEngine.Object.Destroy(persistentSceneProbe);

        yield return null;
    }

    [UnityTest]
    public IEnumerator StartRuntime_PublishesCommittedGlobalServices()
    {
        yield return StartBootstrapAndWaitUntilReady();

        Assert.That(runtimeContext.LifecycleState, Is.EqualTo(ProjectRuntimeLifecycleState.Ready));
        Assert.That(runtimeContext.IsReady, Is.True);
        Assert.That(G.IsReady, Is.True);
        AssertGlobalContractsAvailable();

        AppRuntime appRuntime = GetSinglePersistentComponent<AppRuntime>();
        Assert.That(appRuntime.SceneScopeCount, Is.EqualTo(1));
        Assert.That(
            runtimeContext.StateMachine.CurrentState,
            Is.EqualTo(GameState.MainMenu));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        GlobalServiceDiagnostics diagnostics = G.Diagnostics;

        Assert.That(diagnostics.Generation, Is.GreaterThan(0));
        Assert.That(
            diagnostics.State,
            Is.EqualTo(GlobalServicePublicationState.Ready));
        Assert.That(diagnostics.Owner, Does.Contain(nameof(ProjectContext)));
        Assert.That(diagnostics.Owner, Does.Contain(runtimeContext.gameObject.name));
#endif
    }

    [UnityTest]
    public IEnumerator StartupError_DoesNotPublishGlobalServices()
    {
        GameObject invalidContextObject = new("Invalid ProjectContext");
        UnityEngine.Object.DontDestroyOnLoad(invalidContextObject);
        ProjectContext invalidContext = invalidContextObject.AddComponent<ProjectContext>();
        bool previousIgnoreState = LogAssert.ignoreFailingMessages;

        try
        {
            LogAssert.ignoreFailingMessages = true;

            Assert.That(invalidContext.StartRuntime(), Is.False);
            Assert.That(
                invalidContext.LifecycleState,
                Is.EqualTo(ProjectRuntimeLifecycleState.Disposed));
            Assert.That(G.IsReady, Is.False);
            Assert.That(G.TryResolve(out IUiErrorService service), Is.False);
            Assert.That(service, Is.Null);
        }
        finally
        {
            LogAssert.ignoreFailingMessages = previousIgnoreState;
            UnityEngine.Object.Destroy(invalidContextObject);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator ShutdownCleanup_KeepsGlobalPublicationAvailable()
    {
        yield return StartBootstrapAndWaitUntilReady();

        runtimeContext.ShutdownRuntime();

        Assert.That(
            runtimeContext.LifecycleState,
            Is.EqualTo(ProjectRuntimeLifecycleState.ShuttingDown));
        Assert.That(G.IsReady, Is.True);
        Assert.That(G.Resolve<IUiErrorService>(), Is.Not.Null);
        Assert.That(G.Resolve<IAudioService>(), Is.Not.Null);
    }

    [UnityTest]
    public IEnumerator DisposeRuntime_RemovesGlobalPublication()
    {
        yield return StartBootstrapAndWaitUntilReady();

        runtimeContext.DisposeRuntime();

        Assert.That(
            runtimeContext.LifecycleState,
            Is.EqualTo(ProjectRuntimeLifecycleState.Disposed));
        Assert.That(G.IsReady, Is.False);
        Assert.That(G.TryResolve(out IUiErrorService service), Is.False);
        Assert.That(service, Is.Null);
        Assert.Throws<InvalidOperationException>(() => G.Resolve<IUiErrorService>());
    }

    [UnityTest]
    public IEnumerator RestartWithoutDomainReload_PublishesNewGlobalGeneration()
    {
        yield return StartBootstrapAndWaitUntilReady();

        ProjectContext firstContext = runtimeContext;
        firstContext.DisposeRuntime();

        Assert.That(G.IsReady, Is.False);

        yield return DestroyProjectRuntimeRoots();

        Assert.That(G.IsReady, Is.False);

        yield return StartBootstrapAndWaitUntilReady();

        Assert.That(ReferenceEquals(firstContext, runtimeContext), Is.False);
        Assert.That(runtimeContext.IsReady, Is.True);
        Assert.That(G.IsReady, Is.True);
        AssertGlobalContractsAvailable();
    }

    private IEnumerator StartBootstrapAndWaitUntilReady()
    {
        Assert.That(G.IsReady, Is.False, "G must not be published before Bootstrap starts.");

        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(
            BootstrapScenePath,
            LoadSceneMode.Single);

        Assert.That(loadOperation, Is.Not.Null, "Bootstrap scene could not be loaded.");
        yield return loadOperation;

        yield return WaitForCondition(
            () => G.IsReady,
            "ProjectContext did not publish G after Bootstrap startup.");

        runtimeContext = GetSinglePersistentComponent<ProjectContext>();

        yield return WaitForCondition(
            IsStartupSceneOperationComplete,
            "Bootstrap startup scene operation did not complete.");
    }

    private bool IsStartupSceneOperationComplete()
    {
        return G.TryResolve(out IProjectSceneFlowService sceneFlowService) &&
               !sceneFlowService.HasPendingOperation;
    }

    private static IEnumerator WaitForCondition(
        Func<bool> condition,
        string failureMessage)
    {
        for (int frame = 0; frame < StartupFrameLimit; frame++)
        {
            if (condition.Invoke())
                yield break;

            yield return null;
        }

        Assert.Fail(failureMessage);
    }

    private void AssertGlobalContractsAvailable()
    {
        Assert.That(G.TryResolve(out IProjectSceneRegistry _), Is.True);
        Assert.That(G.TryResolve(out IGameStateService _), Is.True);
        Assert.That(G.TryResolve(out IProjectSceneFlowService _), Is.True);
        Assert.That(G.TryResolve(out INetworkSessionService _), Is.True);
        Assert.That(G.TryResolve(out IUiErrorService _), Is.True);
        Assert.That(G.TryResolve(out IAudioService _), Is.True);
        Assert.That(G.TryResolve(out IGameMapCatalog _), Is.True);
        Assert.Throws<InvalidOperationException>(() =>
            G.TryResolve(out INetworkConnectionService _));
    }

    private T GetSinglePersistentComponent<T>() where T : Component
    {
        List<T> components = new();
        GameObject[] roots = persistentScene.GetRootGameObjects();

        for (int i = 0; i < roots.Length; i++)
        {
            T[] rootComponents = roots[i].GetComponentsInChildren<T>(true);
            components.AddRange(rootComponents);
        }

        Assert.That(
            components.Count,
            Is.EqualTo(1),
            $"Expected exactly one persistent {typeof(T).Name}.");

        return components[0];
    }

    private IEnumerator DestroyProjectRuntimeRoots()
    {
        if (!persistentScene.IsValid())
            yield break;

        GameObject[] roots = persistentScene.GetRootGameObjects();
        List<GameObject> runtimeRoots = new();

        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];

            if (root != null && IsProjectRuntimeRoot(root))
                runtimeRoots.Add(root);
        }

        for (int i = 0; i < runtimeRoots.Count; i++)
        {
            ProjectContext[] contexts =
                runtimeRoots[i].GetComponentsInChildren<ProjectContext>(true);

            for (int contextIndex = 0; contextIndex < contexts.Length; contextIndex++)
                contexts[contextIndex]?.DisposeRuntime();
        }

        for (int i = 0; i < runtimeRoots.Count; i++)
        {
            if (runtimeRoots[i] != null)
                UnityEngine.Object.Destroy(runtimeRoots[i]);
        }

        if (runtimeRoots.Count > 0)
            yield return null;
    }

    private static bool IsProjectRuntimeRoot(GameObject root)
    {
        return root.GetComponentInChildren<ProjectContext>(true) != null ||
               root.GetComponentInChildren<AppRuntime>(true) != null ||
               root.GetComponentInChildren<AudioManager>(true) != null ||
               root.GetComponentInChildren<UiErrorManager>(true) != null;
    }
}
