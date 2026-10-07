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

    // Finer than the default, in small tiles: the navmesh is otherwise a few
    // large flat polygons, one of which ran from the foot of a flight of
    // stairs out across the landing a quarter of a metre under its floor.
    [Test]
    public void EverySurface_IsBuiltFineAndInSmallTiles()
    {
        GameObject navigation = new("Navigation");

        try
        {
            NavMeshSurface surface = navigation.AddComponent<NavMeshSurface>();
            surface.overrideVoxelSize = false;
            surface.overrideTileSize = false;
            RuntimeNavMeshBuilder builder = navigation.AddComponent<RuntimeNavMeshBuilder>();

            PlayModeTestReflection.Invoke(builder, "ConfigureSurface", surface);

            float radius = UnityEngine.AI.NavMesh.GetSettingsByID(surface.agentTypeID).agentRadius;
            Assert.That(surface.overrideVoxelSize, Is.True);
            Assert.That(surface.voxelSize, Is.EqualTo(radius / 6f).Within(0.0001f));
            Assert.That(surface.overrideTileSize, Is.True);
            Assert.That(surface.tileSize, Is.EqualTo(16));
        }
        finally
        {
            Object.DestroyImmediate(navigation);
        }
    }

    // What is baked in the editor is what the game builds. A surface as it
    // is added builds from what is drawn, on every layer, without a height
    // mesh: the navmesh shown over a flight of stairs floated above the
    // treads, and was not the one the enemy walked on.
    [Test]
    public void SurfacesInTheEditor_AreSetAsTheGameBuildsThem()
    {
        GameObject navigation = new("Navigation");

        try
        {
            NavMeshSurface surface = navigation.AddComponent<NavMeshSurface>();
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.RenderMeshes;
            surface.layerMask = ~0;
            surface.buildHeightMesh = false;
            RuntimeNavMeshBuilder builder = navigation.AddComponent<RuntimeNavMeshBuilder>();

            PlayModeTestReflection.Invoke(builder, "OnValidate");

            Assert.That(surface.useGeometry, Is.EqualTo(UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders));
            Assert.That(surface.layerMask.value, Is.EqualTo(PlayModeTestReflection.GetField<LayerMask>(builder, "includedLayers").value));
            Assert.That(surface.buildHeightMesh, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(navigation);
        }
    }
}
