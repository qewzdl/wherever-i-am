using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;

// What every map's navigation is built with, whatever its surfaces say.
public sealed class RuntimeNavMeshBuilderSettingsTests
{
    // The floor's own height. Without a height mesh the navmesh lies a few
    // centimetres above the floor, and the enemy, held to it, stood that far
    // above every floor of every map.
    [Test]
    public void EverySurface_IsBuiltWithTheFloorsOwnHeight()
    {
        GameObject navigation = new("Navigation");

        try
        {
            NavMeshSurface surface = navigation.AddComponent<NavMeshSurface>();
            surface.buildHeightMesh = false;
            RuntimeNavMeshBuilder builder = navigation.AddComponent<RuntimeNavMeshBuilder>();

            PlayModeTestReflection.Invoke(builder, "ConfigureSurface", surface);

            Assert.That(surface.buildHeightMesh, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(navigation);
        }
    }
}
