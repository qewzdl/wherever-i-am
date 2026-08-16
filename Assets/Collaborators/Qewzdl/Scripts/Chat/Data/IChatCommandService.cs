public interface IChatCommandService
{
    void SubmitMessage(string text);

    // Server side only; a client calling it does nothing.
    void AddSystemMessage(string text);
}