namespace SessionGuard.Infrastructure.Ipc;

public static class SessionControlProtocol
{
    public const string Version = "1.2";
    public const string RequestPayloadType = "session-control-request";
    public const string ResponsePayloadType = "session-control-response";
    public const int MaxEnvelopeBytes = 1024 * 1024;
}
