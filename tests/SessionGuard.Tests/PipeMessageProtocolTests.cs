using System.Buffers.Binary;
using SessionGuard.Infrastructure.Ipc;

namespace SessionGuard.Tests;

public sealed class PipeMessageProtocolTests
{
    [Fact]
    public async Task ReadRequestAsync_RejectsOversizedPayload()
    {
        var stream = new MemoryStream();
        var lengthBuffer = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, SessionControlProtocol.MaxEnvelopeBytes + 1);
        await stream.WriteAsync(lengthBuffer);
        stream.Position = 0;

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => PipeMessageProtocol.ReadRequestAsync(stream));

        Assert.Contains("maximum allowed size", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
