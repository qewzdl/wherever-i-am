using UnityEngine;

// Ears.
//
// Without them a noise is nothing: she will not come to look at a slammed door,
// will not hear somebody run past behind her, and can be walked around in the
// dark by anybody willing to stay out of her cone. Sight is the sense with
// range and a blind spot; sound is the one that comes through walls, so taking
// it away makes an enemy you can outmanoeuvre rather than one you must outrun.
//
// Was a tick box on the hearing profile - hearingEnabled - which is the same
// switch in the place where the tuning lives. It has gone: whether she hears
// at all is a behaviour, and how far and how well are settings.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Behaviors/Hearing",
    fileName = "EnemyHearingBehavior"
)]
public sealed class EnemyHearingModule : EnemyBehaviorModule
{
    public override void Install(EnemyBehaviorInstaller installer)
    {
        installer.Context.TargetDetector?.InstallHearing();
    }
}
