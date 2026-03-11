namespace SessionGuard.Infrastructure.Ipc;

public sealed record SessionPipeEnvelope<T>(
    string ProtocolVersion,
    string PayloadType,
    T Payload);
