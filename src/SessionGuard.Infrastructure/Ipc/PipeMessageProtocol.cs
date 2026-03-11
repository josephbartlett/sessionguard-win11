using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using SessionGuard.Infrastructure.Serialization;

namespace SessionGuard.Infrastructure.Ipc;

public static class PipeMessageProtocol
{
    public static async Task WriteAsync<T>(
        Stream stream,
        T value,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, SessionGuardJson.Default);
        var lengthBuffer = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, payload.Length);

        await stream.WriteAsync(lengthBuffer, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<T> ReadAsync<T>(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var lengthBuffer = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, lengthBuffer, cancellationToken);
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);

        if (payloadLength <= 0)
        {
            throw new InvalidDataException("Received an invalid pipe payload length.");
        }

        var payload = new byte[payloadLength];
        await ReadExactlyAsync(stream, payload, cancellationToken);

        return JsonSerializer.Deserialize<T>(payload, SessionGuardJson.Default) ??
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
