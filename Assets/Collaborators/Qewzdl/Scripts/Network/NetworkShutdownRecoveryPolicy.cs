using System;
using System.Threading.Tasks;

internal static class NetworkShutdownRecoveryPolicy
{
    internal static async Task ExecuteAsync(
        Func<NetworkShutdownMode, Task> shutdown,
        NetworkShutdownMode initialMode,
        int additionalAttempts,
        Action<int, int> retrying = null)
    {
        if (shutdown == null)
            throw new ArgumentNullException(nameof(shutdown));

        int attemptCount = Math.Max(0, additionalAttempts) + 1;
        TimeoutException lastTimeout = null;

        for (int attempt = 0; attempt < attemptCount; attempt++)
        {
            try
            {
                NetworkShutdownMode attemptMode = attempt == 0
                    ? initialMode
                    : NetworkShutdownMode.Immediate;

                await shutdown.Invoke(attemptMode);
                return;
            }
            catch (TimeoutException exception)
            {
                lastTimeout = exception;

                if (attempt + 1 >= attemptCount)
                    break;

                retrying?.Invoke(attempt + 2, attemptCount);
                await Task.Yield();
            }
        }

        throw new TimeoutException(
            $"Network shutdown did not reach callback-confirmed completion " +
            $"after {attemptCount} attempt(s). Session cleanup remains " +
            "fail-closed and can be retried.",
            lastTimeout);
    }
}
