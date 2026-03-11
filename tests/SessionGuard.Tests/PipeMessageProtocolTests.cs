using SessionGuard.Core.Models;
using SessionGuard.Infrastructure.Ipc;

namespace SessionGuard.Tests;

public sealed class PipeMessageProtocolTests
{
    [Fact]
    public async Task WriteAndReadAsync_RoundTripsControlRequest()
    {
        await using var stream = new MemoryStream();
        var request = new SessionControlRequest(SessionControlCommandType.SetGuardMode, GuardModeEnabled: true);

        await PipeMessageProtocol.WriteAsync(stream, request);
        stream.Position = 0;

        var roundTripped = await PipeMessageProtocol.ReadAsync<SessionControlRequest>(stream);

        Assert.Equal(request, roundTripped);
    }

    [Fact]
    public async Task WriteAndReadAsync_RoundTripsControlResponse()
    {
        await using var stream = new MemoryStream();
        var response = new SessionControlResponse(
            true,
            "Scan completed.",
            new SessionControlStatus(
                new SessionScanResult(
                    DateTimeOffset.Now,
                    RestartStateCategory.MitigatedDeferred,
                    RestartRiskLevel.Low,
                    ProtectionMode.ManagedMitigationsApplied,
                    RestartPending: false,
                    HasAmbiguousSignals: false,
                    ProtectedSessionActive: false,
                    LimitedVisibility: false,
                    IsElevated: true,
                    Summary: "Managed mitigations are applied.",
                    new RestartSignalOverview(1, 1, 0, 0, 0, 1, 0, "No restart pending."),
                    new[]
                    {
                        new RestartIndicator(
                            "stub",
                            "Mitigation visibility",
                            RestartIndicatorCategory.MitigationVisibility,
                            true,
                            "Managed mitigations are visible.",
                            SignalConfidence.Medium)
                    },
                    Array.Empty<ProtectedProcessMatch>(),
                    new[]
                    {
                        new ManagedMitigationState(
                            "no-auto-reboot",
                            "No auto reboot",
                            "Description",
                            true,
                            true,
                            "1",
                            "1",
                            @"HKLM\Software\Test")
                    },
                    new[] { "No action required." }),
                GuardModeEnabled: true,
                "Service",
                IsRemote: true),
            new MitigationCommandResult(
                true,
                false,
                "Applied",
                new[]
                {
                    new ManagedMitigationState(
                        "active-hours",
                        "Active hours",
                        "Description",
                        true,
                        true,
                        "8",
                        "8",
                        @"HKLM\Software\Test")
                }));

        await PipeMessageProtocol.WriteAsync(stream, response);
        stream.Position = 0;

        var roundTripped = await PipeMessageProtocol.ReadAsync<SessionControlResponse>(stream);

        Assert.True(roundTripped.Success);
        Assert.Equal("Scan completed.", roundTripped.Message);
        Assert.NotNull(roundTripped.Status);
        Assert.Equal("Service", roundTripped.Status!.ConnectionMode);
        Assert.Equal(RestartStateCategory.MitigatedDeferred, roundTripped.Status.ScanResult.State);
        Assert.NotNull(roundTripped.MitigationResult);
        Assert.Single(roundTripped.MitigationResult!.CurrentStates);
    }
}
