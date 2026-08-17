using System;
using System.Text;
using UnityEngine;

internal readonly struct NetworkConnectionPayload
{
    internal ushort ProtocolVersion { get; }
    internal string BuildVersion { get; }
    internal string PlayerId { get; }

    // What this player calls themselves. A label, not an identity: the id above
    // is what a reconnect is matched on, and nothing is trusted to a name.
    internal string PlayerName { get; }

    internal NetworkConnectionPayload(
        ushort protocolVersion,
        string buildVersion,
        string playerId,
        string playerName = "")
    {
        ProtocolVersion = protocolVersion;
        BuildVersion = buildVersion;
        PlayerId = playerId;
        PlayerName = playerName ?? string.Empty;
    }
}

public static class NetworkConnectionPayloadCodec
{
    internal const int SchemaVersion = 1;
    internal const int MaximumPayloadBytes = 256;
    internal const int MaximumBuildVersionLength = 64;

    // FixedString32Bytes carries the name from here to the lobby list and the
    // chat, and it holds 29 bytes of UTF-8 - which is fourteen Cyrillic
    // characters, not twenty-nine. Cutting by bytes is the only cut that fits.
    internal const int MaximumPlayerNameBytes = 29;

    internal static bool TryEncode(
        ushort protocolVersion,
        string buildVersion,
        string playerId,
        out byte[] payload,
        out string error)
    {
        return TryEncode(
            protocolVersion,
            buildVersion,
            playerId,
            string.Empty,
            out payload,
            out error);
    }

    internal static bool TryEncode(
        ushort protocolVersion,
        string buildVersion,
        string playerId,
        string playerName,
        out byte[] payload,
        out string error)
    {
        payload = null;

        if (!TryValidate(
                protocolVersion,
                buildVersion,
                playerId,
                out string normalizedPlayerId,
                out error))
        {
            return false;
        }

        PayloadData data = new()
        {
            schemaVersion = SchemaVersion,
            protocolVersion = protocolVersion,
            buildVersion = buildVersion.Trim(),
            playerId = normalizedPlayerId,
            playerName = NormalizePlayerName(playerName)
        };

        payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(data));

        if (payload.Length <= MaximumPayloadBytes)
            return true;

        error =
            $"Connection payload is {payload.Length} bytes; maximum is " +
            $"{MaximumPayloadBytes}.";
        payload = null;
        return false;
    }

    internal static bool TryDecode(
        byte[] payload,
        out NetworkConnectionPayload connectionPayload,
        out string error)
    {
        connectionPayload = default;

        if (payload == null || payload.Length == 0)
        {
            error = "Connection payload is missing.";
            return false;
        }

        if (payload.Length > MaximumPayloadBytes)
        {
            error =
                $"Connection payload is {payload.Length} bytes; maximum is " +
                $"{MaximumPayloadBytes}.";
            return false;
        }

        PayloadData data;

        try
        {
            data = JsonUtility.FromJson<PayloadData>(
                Encoding.UTF8.GetString(payload));
        }
        catch (ArgumentException)
        {
            error = "Connection payload is not valid JSON.";
            return false;
        }

        if (data == null || data.schemaVersion != SchemaVersion)
        {
            error = "Connection payload schema is unsupported.";
            return false;
        }

        if (!TryValidate(
                data.protocolVersion,
                data.buildVersion,
                data.playerId,
                out string normalizedPlayerId,
                out error))
        {
            return false;
        }

        connectionPayload = new NetworkConnectionPayload(
            data.protocolVersion,
            data.buildVersion.Trim(),
            normalizedPlayerId,
            NormalizePlayerName(data.playerName));
        return true;
    }

    internal static bool TryNormalizePlayerId(
        string playerId,
        out string normalizedPlayerId)
    {
        normalizedPlayerId = string.Empty;

        if (!Guid.TryParseExact(playerId, "N", out Guid parsed))
            return false;

        normalizedPlayerId = parsed.ToString("N");
        return true;
    }

    // Never a reason to refuse a connection: a name nobody can use just leaves
    // the host to fall back on one of its own. What must not survive is markup
    // - TextMeshPro reads tags, so a player called "<color=red><size=300>"
    // would rewrite the chat and the lobby list for everybody else.
    public static string NormalizePlayerName(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return string.Empty;

        StringBuilder builder = new StringBuilder(playerName.Length);

        foreach (char character in playerName.Trim())
        {
            if (char.IsControl(character) || character == '<' || character == '>')
                continue;

            builder.Append(character);
        }

        return TruncateToBytes(builder.ToString().Trim(), MaximumPlayerNameBytes);
    }

    private static string TruncateToBytes(string value, int maximumBytes)
    {
        if (string.IsNullOrEmpty(value) ||
            Encoding.UTF8.GetByteCount(value) <= maximumBytes)
        {
            return value;
        }

        int length = value.Length;

        while (length > 0 &&
               Encoding.UTF8.GetByteCount(value, 0, length) > maximumBytes)
        {
            length--;
        }

        return value.Substring(0, length).TrimEnd();
    }

    private static bool TryValidate(
        ushort protocolVersion,
        string buildVersion,
        string playerId,
        out string normalizedPlayerId,
        out string error)
    {
        normalizedPlayerId = string.Empty;

        if (protocolVersion == 0)
        {
            error = "Connection payload protocol version is invalid.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(buildVersion) ||
            buildVersion.Trim().Length > MaximumBuildVersionLength)
        {
            error = "Connection payload build version is invalid.";
            return false;
        }

        if (!TryNormalizePlayerId(playerId, out normalizedPlayerId))
        {
            error = "Connection payload player id is invalid.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    [Serializable]
    private sealed class PayloadData
    {
        public int schemaVersion;
        public ushort protocolVersion;
        public string buildVersion;
        public string playerId;
        public string playerName;
    }
}
