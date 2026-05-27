using UnityEngine;

public abstract class PauseServiceConsumer : MonoBehaviour, IPauseServiceConsumer
{
    protected IPauseService PauseService { get; private set; }

    public void BindPauseService(IPauseService pauseService)
    {
        if (ReferenceEquals(PauseService, pauseService))
        {
            OnPauseServiceRebound(pauseService);
            return;
        }

        IPauseService previousService = PauseService;

        if (previousService != null)
            OnBeforePauseServiceUnbound(previousService);

        PauseService = pauseService;

        if (PauseService != null)
            OnAfterPauseServiceBound(PauseService);
    }

    protected void ClearPauseService()
    {
        BindPauseService(null);
    }

    protected virtual void OnPauseServiceRebound(IPauseService pauseService)
    {
    }

    protected virtual void OnAfterPauseServiceBound(IPauseService pauseService)
    {
    }

    protected virtual void OnBeforePauseServiceUnbound(IPauseService pauseService)
    {
    }
}