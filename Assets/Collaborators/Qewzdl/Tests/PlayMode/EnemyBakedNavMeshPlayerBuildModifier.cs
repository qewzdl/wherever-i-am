#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.TestTools;

[assembly: TestPlayerBuildModifier(
    typeof(EnemyBakedNavMeshPlayerBuildModifier))]

internal sealed class EnemyBakedNavMeshPlayerBuildModifier :
    ITestPlayerBuildModifier
{
    private const string FixtureScenePath =
        "Assets/Collaborators/Qewzdl/Tests/PlayMode/Scenarios/EnemyBakedNavMeshFixture.unity";

    public BuildPlayerOptions ModifyOptions(
        BuildPlayerOptions playerOptions)
    {
        List<string> scenes = new(
            playerOptions.scenes ?? Array.Empty<string>());

        if (!scenes.Contains(FixtureScenePath))
            scenes.Add(FixtureScenePath);

        playerOptions.scenes = scenes.ToArray();
        return playerOptions;
    }
}
#endif
