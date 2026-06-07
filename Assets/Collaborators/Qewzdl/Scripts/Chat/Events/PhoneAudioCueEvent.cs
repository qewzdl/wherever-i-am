public readonly struct PhoneAudioCueEvent
{
    public PhoneAudioCueType CueType { get; }
    public uint MessageId { get; }

    public bool HasMessageId => MessageId != 0;

    private PhoneAudioCueEvent(
        PhoneAudioCueType cueType,
        uint messageId)
    {
        CueType = cueType;
        MessageId = messageId;
    }

    public static PhoneAudioCueEvent IncomingNotification(uint messageId)
    {
        return new PhoneAudioCueEvent(
            PhoneAudioCueType.IncomingNotification,
            messageId
        );
    }

    public static PhoneAudioCueEvent Open()
    {
        return new PhoneAudioCueEvent(PhoneAudioCueType.Open, 0);
    }

    public static PhoneAudioCueEvent Close()
    {
        return new PhoneAudioCueEvent(PhoneAudioCueType.Close, 0);
    }

    public static PhoneAudioCueEvent Input()
    {
        return new PhoneAudioCueEvent(PhoneAudioCueType.Input, 0);
    }
}
