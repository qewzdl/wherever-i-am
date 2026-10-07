using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// The step check that map validation runs over a map's own floors: a step
// higher than the enemy can climb is named, one she can climb is not, and a
// wall is not a step.
public sealed class NavigationStepCheckTests
{
    // The enemy's climb, as the project sets it for her standing navmesh.
    private const float Climb = 0.4f;

    private static List<string> CheckWithStep(float height, float width = 2f)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();

        try
        {
            CreateBox(scene, "Floor", new Vector3(0f, -0.1f, 0f), new Vector3(12f, 0.2f, 12f));
            CreateBox(scene, "Step", new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, width));

            List<string> warnings = new List<string>();
            NavigationStepCheck.Find(scene, ~0, warnings);
            return warnings;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void CreateBox(Scene scene, string name, Vector3 position, Vector3 size)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.position = position;
        box.transform.localScale = size;
        SceneManager.MoveGameObjectToScene(box, scene);
    }

    [Test]
    public void AStepTallerThanTheEnemyCanClimb_IsReported()
    {
        List<string> warnings = CheckWithStep(0.5f);

        Assert.That(warnings, Has.Count.GreaterThan(0), "A 0.5 m step, above her 0.4 m climb, was not reported.");
        Assert.That(warnings[0], Does.Contain("0.50 m step").Or.Contain("0,50 m step"));
    }

    [Test]
    public void AStepSheCanClimb_IsNotReported()
    {
        Assert.That(CheckWithStep(0.3f), Is.Empty, "A 0.3 m step, under her climb, was reported.");
    }

    [Test]
    public void AStepJustUnderHerClimb_IsNotReported()
    {
        Assert.That(CheckWithStep(0.39f), Is.Empty, "A 0.39 m step, the stairs on Map_New_Map, was reported.");
    }

    [Test]
    public void AWall_IsNotReportedAsAStep()
    {
        Assert.That(CheckWithStep(2f), Is.Empty, "A 2 m wall was reported as a step.");
    }
}
