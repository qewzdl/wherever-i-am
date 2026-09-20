using UnityEngine;

// Getting down on her hands and knees to follow you through a low gap.
//
// Was crawlingEnabled on the posture profile - a capability filed with the
// numbers that tune it. How low a gap has to be and how long she takes to get
// down are settings; whether she will do it at all is a behaviour.
//
// Two install points for one behaviour, which is a wart worth explaining.
// Everything that routes through crawling reads it off the posture controller
// it already holds - the navigator and the traversal planner both do - but the
// patrol controller plans crawling routes and holds no posture controller to
// ask. So it gets its own copy, set from here, which is the only way the two
// cannot drift apart.
//
// Without it she is a standing-height enemy: the gap under the stairs stops
// being a shortcut she can follow you through and starts being a way out.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Behaviors/Crawling",
    fileName = "EnemyCrawlingBehavior"
)]
public sealed class EnemyCrawlingModule : EnemyBehaviorModule
{
    public override void Install(EnemyBehaviorInstaller installer)
    {
        installer.Context.PostureController?.InstallCrawling();
        installer.Context.PatrolController?.InstallCrawling();
    }
}
