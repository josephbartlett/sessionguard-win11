using System.Buffers.Binary;
using System.Text.Json;
using SessionGuard.Core.Models;
using SessionGuard.Infrastructure.Serialization;

namespace SessionGuard.Infrastructure.Ipc;

public static class PipeMessageProtocol
{
    public static Task WriteRequestAsync(
        Stream stream,
        SessionControlRequest request,
        CancellationToken cancellationToken = default)
    {
        return WriteEnvelopeAsync(
            stream,
            SessionControlProtocol.RequestPayloadType,
            request,
            cancellationToken);
    }

    public static Task<SessionControlRequest> ReadRequestAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        return ReadEnvelopeAsync<SessionControlRequest>(
            stream,
            SessionControlProtocol.RequestPayloadType,
            cancellationToken);
    }

    public static Task WriteResponseAsync(
        Stream stream,
        SessionControlResponse response,
        CancellationToken cancellationToken = default)
    {
        return WriteEnvelopeAsync(
            stream,
            SessionControlProtocol.ResponsePayloadType,
            response,
            cancellationToken);
    }

    public static Task<SessionControlResponse> ReadResponseAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        return ReadEnvelopeAsync<SessionControlResponse>(
            stream,
            SessionControlProtocol.ResponsePayloadType,
            cancellationToken);
    }

    private static async Task WriteEnvelopeAsync<T>(
        Stream stream,
        string payloadType,
        T value,
        CancellationToken cancellationToken)
    {
        var envelope = new SessionPipeEnvelope<T>(
            SessionControlProtocol.Version,
            payloadType,
            value);
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, SessionGuardJson.Default);
        var lengthBuffer = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, payload.Length);

        await stream.WriteAsync(lengthBuffer, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<T> ReadEnvelopeAsync<T>(
        Stream stream,
        string expectedPayloadType,
        CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, lengthBuffer, cancellationToken);
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);

        if (payloadLength <= 0)
        {
            throw new InvalidDataException("Received an invalid pipe payload length.");
        }

        if (payloadLength > SessionControlProtocol.MaxEnvelopeBytes)
        {
            throw new InvalidDataException(
                $"Received a pipe payload length of {payloadLength} bytes, which exceeds the maximum allowed size of {SessionControlProtocol.MaxEnvelopeBytes} bytes.");
        }

        var payload = new byte[payloadLength];
        await ReadExactlyAsync(stream, payload, cancellationToken);

        var envelope = JsonSerializer.Deserialize<SessionPipeEnvelope<JsonElement>>(payload, SessionGuardJson.Default) ??
                       throw new InvalidDataException("Failed to deserialize the pipe envelope.");

        if (!string.Equals(envelope.ProtocolVersion, SessionControlProtocol.Version, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported SessionGuard control-plane protocol version '{envelope.ProtocolVersion}'.");
        }

        if (!string.Equals(envelope.PayloadType, expectedPayloadType, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unexpected pipe payload type '{envelope.PayloadType}'. Expected '{expectedPayloadType}'.");
        }

        return envelope.Payload.Deserialize<T>(SessionGuardJson.Default) ??
               throw new InvalidDataException($"Failed to deserialize pipe payload to {typeof(T).Name}.");
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;

        while (offset < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset),
                cancellationToken);

            if (bytesRead == 0)
            {
                throw new EndOfStreamException("Pipe stream ended before the full payload was read.");
            }

            offset += bytesRead;
        }
    }
}
