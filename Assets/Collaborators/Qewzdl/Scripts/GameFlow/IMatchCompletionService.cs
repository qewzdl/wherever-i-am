using System;

public interface IMatchCompletionService
{
    bool IsMatchRunning { get; }

    // The read side exists for whoever has to tell the players what happened;
    // the match is resolved and then taken away within a couple of seconds, so
    // asking after the fact is not an option.
    GameResultData CurrentResult { get; }
    event Action<GameResultData> MatchResolved;

    bool CompleteMatchServerOnly(GameResultData matchResult, string reason);
}
