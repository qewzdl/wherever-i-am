public sealed class ChatMessageValidator
{
    private const int AbsoluteMaxMessageLength = 120;

    public bool TryNormalize(
        string rawText,
        int maxMessageLength,
        out string normalizedText)
    {
        normalizedText = string.Empty;

        if (string.IsNullOrWhiteSpace(rawText))
            return false;

        int safeMaxLength = ClampMaxLength(maxMessageLength);

        string text = rawText
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();

        while (text.Contains("  "))
            text = text.Replace("  ", " ");

        if (text.Length > safeMaxLength)
            text = text.Substring(0, safeMaxLength).Trim();

        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text
            .Replace("<", "[")
            .Replace(">", "]");

        normalizedText = text;
        return true;
    }

    private int ClampMaxLength(int value)
    {
        if (value < 1)
            return 1;

        if (value > AbsoluteMaxMessageLength)
            return AbsoluteMaxMessageLength;

        return value;
    }
}
