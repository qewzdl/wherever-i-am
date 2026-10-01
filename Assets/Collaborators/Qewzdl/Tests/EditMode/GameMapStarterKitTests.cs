using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// A map made in the Map Manager is one somebody can walk into at once. It
// used to be a root, a spawn point and no objectives, and a match will not
// start on a map that cannot finish - so a new map would not start at all.
public sealed class GameMapStarterKitTests
{
    private const string Folder = "Assets/StarterKitTest_Temp";
    private const string ScenePath = Folder + "/Map_KitTest.unity";

    // Standing in an untitled scene, as anybody who has just opened the editor
    // is. The map tools used to fail there twice over: making the map beside
    // it ("Cannot create a new scene additively with an untitled scene
    // unsaved"), and putting the open scenes back afterwards ("Invalid
    // SceneManagerSetup: One active scene is required").
    [SetUp]
    public void MakeFolder()
    {
        AssetDatabase.DeleteAsset(Folder);
        AssetDatabase.CreateFolder("Assets", "StarterKitTest_Temp");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    [TearDown]
    public void RemoveFolder()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AssetDatabase.DeleteAsset(Folder);
    }

    [Test]
    public void ANewMap_IsReadyToPlay()
    {
        GameMapEditorUtility.CreateEmptyMapScene(ScenePath, "KitTest");
        GameMapStarterKit.Complete(ScenePath, DefaultSequence(), addFloor: true);

        ObjectiveSequenceDefinition sequence = DefaultSequence();
        Scene scene = EditorSceneManager.OpenPreviewScene(ScenePath);

        try
        {
            GameMapRoot root = FindRoot(scene);

            Assert.That(root.ObjectiveBindingRegistry.IsValidForSequence(sequence, out string error), Is.True,
                "The match would not start: " + error);
            Assert.That(root.PlayerSpawnPointCount, Is.GreaterThanOrEqualTo(4),
                "Not every player in a full lobby has somewhere to arrive.");
            Assert.That(root.EnemySpawnPoints.Count, Is.EqualTo(1));
            Assert.That(root.EnemySpawnPoints[0].EnemyPrefab, Is.Not.Null, "The enemy spawn point names no enemy.");
            Assert.That(root.NavMeshBuilders.Count, Is.EqualTo(1), "Nothing would build her navigation.");

            foreach (EntranceDoorObjectiveReporter reporter in
                     root.GetComponentsInChildren<EntranceDoorObjectiveReporter>(true))
            {
                Assert.That(new SerializedObject(reporter).FindProperty("entranceDoor").objectReferenceValue,
                    Is.Not.Null, "An objective was given no door to report on.");
            }

            List<NetworkObject> networkObjects = new();

            foreach (GameObject rootObject in scene.GetRootGameObjects())
                networkObjects.AddRange(rootObject.GetComponentsInChildren<NetworkObject>(true));

            Assert.That(networkObjects, Is.Not.Empty, "No door or handle was placed.");

            foreach (NetworkObject networkObject in networkObjects)
            {
                Assert.That(PrefabUtility.IsPartOfPrefabInstance(networkObject), Is.False,
                    $"{networkObject.name} is still a prefab instance, which a build refuses.");
                Assert.That(networkObject.PrefabIdHash, Is.Not.Zero, $"{networkObject.name} has no network identity.");
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    // A map already furnished keeps what it has: running it again adds nothing.
    [Test]
    public void AMapWithEverything_IsLeftAlone()
    {
        GameMapEditorUtility.CreateEmptyMapScene(ScenePath, "KitTest");

        Assert.That(GameMapStarterKit.Complete(ScenePath, DefaultSequence(), addFloor: true),
            Has.Some.Contains("entrance door"), "The first pass furnished nothing to leave alone.");
        // From an untitled scene again, so completing has to open the map
        // beside it.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        Assert.That(GameMapStarterKit.Complete(ScenePath, DefaultSequence(), addFloor: false), Is.Empty);
    }

    private static ObjectiveSequenceDefinition DefaultSequence()
    {
        ObjectiveSequenceDefinition sequence = GameMapEditorUtility.LoadDefaultObjectiveSequence();
        Assert.That(sequence, Is.Not.Null, "The game has no objective sequence to build a map for.");
        return sequence;
    }

    private static GameMapRoot FindRoot(Scene scene)
    {
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            GameMapRoot root = rootObject.GetComponentInChildren<GameMapRoot>(true);

            if (root != null)
                return root;
        }

        Assert.Fail("The map has no GameMapRoot.");
        return null;
    }
}
