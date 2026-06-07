using System;
using UnityEngine;

[CreateAssetMenu(
    fileName = "PhoneAudioCueEventChannel",
    menuName = "Wherever I Am/Chat/Phone Audio Cue Event Channel")]
public sealed class PhoneAudioCueEventChannel : ScriptableObject
{
    public event Action<PhoneAudioCueEvent> CuePlayed;

    public bool RaiseCuePlayed(PhoneAudioCueEvent cueEvent)
    {
        if (cueEvent.CueType == PhoneAudioCueType.Unknown)
        {
            return false;
        }

        if (cueEvent.CueType == PhoneAudioCueType.IncomingNotification &&
            !cueEvent.HasMessageId)
        {
            return false;
        }

        CuePlayed?.Invoke(cueEvent);
        return true;
    }
}
