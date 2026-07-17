using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

internal sealed class DestroyTrackingSceneRuntimeFeature : SceneRuntimeFeature
{
    private IList<string> lifecycleEvents;

    internal void Configure(IList<string> events)
    {
        lifecycleEvents = events;
    }

    protected override bool ValidateFeature(SceneFeatureContext context)
    {
        return true;
    }

    protected override bool InstallFeature(SceneFeatureContext context)
    {
        lifecycleEvents.Add("install");
        return true;
    }

    protected override void UninstallFeature(SceneFeatureContext context)
    {
        lifecycleEvents.Add("uninstall");
    }
}

public sealed class SceneRuntimeFeatureLifecyclePlayModeTests
{
    [UnityTest]
    public IEnumerator DestroyInstalledFeature_RequestsOwningScopeUninstall()
    {
        List<string> lifecycleEvents = new();
        ServiceScope globalScope = new("Global");
        ServiceScope sceneServiceScope = globalScope.CreateChild(
            "Scene[63]",
            SceneContractPolicy.Game);
        GameObject featureObject = new("Destroying scene feature");
        SceneRuntimeScope runtimeScope = null;
        int uninstallRequestCount = 0;

        try
        {
            DestroyTrackingSceneRuntimeFeature feature =
                featureObject.AddComponent<DestroyTrackingSceneRuntimeFeature>();
            feature.Configure(lifecycleEvents);
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

            Object.Destroy(featureObject);
            yield return null;

            Assert.That(featureObject == null, Is.True);
            Assert.That(uninstallRequestCount, Is.EqualTo(1));
            Assert.That(runtimeScope.IsReady, Is.False);
            Assert.That(runtimeScope.Services, Is.Null);
            Assert.That(globalScope.ChildScopeCount, Is.Zero);
            CollectionAssert.AreEqual(
                new[] { "install", "uninstall" },
                lifecycleEvents);
        }
        finally
        {
            runtimeScope?.Dispose();

            if (featureObject != null)
                Object.Destroy(featureObject);

            globalScope.Dispose();
        }
    }
}
