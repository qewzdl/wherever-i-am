using System;

/// <summary>
/// The thing the game shows before it shows anything else.
/// </summary>
/// <remarks>
/// A contract rather than a direct reference because the runtime's job is to
/// decide when the game starts, not to know that a video exists. It asks once,
/// and an intro that has nothing to play says so by returning false - which is
/// the same answer a project with no intro at all gives, so the branch does not
/// have to be written twice.
/// </remarks>
public interface IStartupIntro
{
    /// <summary>
    /// Begins the intro and returns whether it took the job.
    /// </summary>
    /// <param name="completed">
    /// Called once when the intro is over, and never if this returns false. The
    /// game continues from here, so nothing may swallow it: a clip that fails
    /// to open, a codec the machine does not have, or a file that was deleted
    /// all have to end up calling this rather than leaving a black screen.
    /// </param>
    bool TryPlay(Action completed);
}
