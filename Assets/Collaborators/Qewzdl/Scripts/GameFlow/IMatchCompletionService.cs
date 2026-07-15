public interface IMatchCompletionService
{
    bool IsMatchRunning { get; }

    bool CompleteMatchServerOnly(GameResultData matchResult, string reason);
}
