using System;
using System.Text;
using UnityEngine;

internal readonly struct NetworkConnectionPayload
{
    internal ushort ProtocolVersion { get; }
    internal string BuildVersion { get; }
    internal string PlayerId { get; }

    internal NetworkConnectionPayload(
        ushort protocolVersion,
        string buildVersion,
        string playerId)
    {
        ProtocolVersion = protocolVersion;
        BuildVersion = buildVersion;
        PlayerId = playerId;
    }
}

internal static class NetworkConnectionPayloadCodec
{
    internal const int SchemaVersion = 1;
    internal const int MaximumPayloadBytes = 256;
    internal const int MaximumBuildVersionLength = 64;

    internal static bool TryEncode(
        ushort protocolVersion,
        string buildVersion,
        string playerId,
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
            playerId = normalizedPlayerId
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
            normalizedPlayerId);
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
    }
}
