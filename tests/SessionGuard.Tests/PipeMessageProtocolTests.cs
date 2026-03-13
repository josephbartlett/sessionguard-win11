using System.Text;
using System.Text.Json;
using SessionGuard.Core.Models;
using SessionGuard.Infrastructure.Ipc;
using SessionGuard.Infrastructure.Serialization;

namespace SessionGuard.Tests;

public sealed class PipeMessageProtocolTests
{
    [Fact]
    public async Task WriteAndReadRequestAsync_RoundTripsControlRequest()
    {
        await using var stream = new MemoryStream();
        var request = new SessionControlRequest(SessionControlCommandType.SetGuardMode, GuardModeEnabled: true);

        await PipeMessageProtocol.WriteRequestAsync(stream, request);
        stream.Position = 0;

        var roundTripped = await PipeMessageProtocol.ReadRequestAsync(stream);

        Assert.Equal(request, roundTripped);
    }

    [Fact]
    public async Task WriteAndReadResponseAsync_RoundTripsControlResponse()
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
                    ProtectionMode.PolicyApprovalWindow,
                    RestartPending: false,
                    HasAmbiguousSignals: false,
                    ProtectedSessionActive: false,
                    LimitedVisibility: false,
                    IsElevated: true,
                    Summary: "Managed mitigations are applied.",
                    WorkspaceStateSnapshot.None(DateTimeOffset.Parse("2026-03-11T09:35:00-05:00")),
                    new PolicyEvaluation(
                        PolicyDecisionType.ApprovalActive,
                        HasBlockingRules: false,
                        RequiresApproval: false,
                        ApprovalActive: true,
                        ApprovalExpiresAt: DateTimeOffset.Parse("2026-03-11T10:35:00-05:00"),
                        RecommendedApprovalWindowMinutes: 60,
                        "A temporary policy approval window is active until 3/11/2026 10:35:00 AM.",
                        new[]
                        {
                            new PolicyRuleMatch(
                                "approval-required-restart-pending",
                                "Approval required for restart-pending states",
                                PolicyRuleKind.ApprovalRequired,
                                PolicyRuleOutcome.Approved,
                                30,
                                "A temporary restart approval window is active until 3/11/2026 10:35:00 AM.")
                        },
                        new[]
                        {
                            "Approval required for restart-pending states: A temporary restart approval window is active until 3/11/2026 10:35:00 AM."
                        })
                    {
                        Validation = PolicyValidationReport.Create(
                            new[]
                            {
                                new PolicyValidationIssue(
                                    "policy-rules-disabled",
                                    PolicyValidationSeverity.Information,
                                    "1 rule is disabled and will be skipped during evaluation.")
                            })
                    },
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
                IsRemote: true,
                CanPerformServiceWrites: true),
                    new MitigationCommandResult(
                        true,
                        false,
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
                }),
            new PolicyApprovalCommandResult(
                true,
                false,
                false,
                "Granted approval",
                new PolicyEvaluation(
                    PolicyDecisionType.ApprovalActive,
                    HasBlockingRules: false,
                    RequiresApproval: false,
                    ApprovalActive: true,
                    ApprovalExpiresAt: DateTimeOffset.Parse("2026-03-11T10:35:00-05:00"),
                    RecommendedApprovalWindowMinutes: 60,
                    "A temporary policy approval window is active until 3/11/2026 10:35:00 AM.",
                    Array.Empty<PolicyRuleMatch>(),
                    Array.Empty<string>())));

        await PipeMessageProtocol.WriteResponseAsync(stream, response);
        stream.Position = 0;

        var roundTripped = await PipeMessageProtocol.ReadResponseAsync(stream);

        Assert.True(roundTripped.Success);
        Assert.Equal("Scan completed.", roundTripped.Message);
        Assert.NotNull(roundTripped.Status);
        Assert.Equal("Service", roundTripped.Status!.ConnectionMode);
        Assert.True(roundTripped.Status.CanPerformServiceWrites);
        Assert.Equal(RestartStateCategory.MitigatedDeferred, roundTripped.Status.ScanResult.State);
        Assert.NotNull(roundTripped.MitigationResult);
        Assert.Single(roundTripped.MitigationResult!.CurrentStates);
        Assert.NotNull(roundTripped.PolicyResult);
        Assert.True(roundTripped.PolicyResult!.Policy.ApprovalActive);
        Assert.True(roundTripped.Status.ScanResult.Policy.Validation.HasIssues);
    }

    [Fact]
    public async Task ReadRequestAsync_RejectsUnsupportedProtocolVersion()
    {
        await using var stream = new MemoryStream();
        await WriteRawEnvelopeAsync(
            stream,
            new SessionPipeEnvelope<SessionControlRequest>(
                "99.0",
                SessionControlProtocol.RequestPayloadType,
                new SessionControlRequest(SessionControlCommandType.GetStatus)));
        stream.Position = 0;

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => PipeMessageProtocol.ReadRequestAsync(stream));

        Assert.Contains("protocol version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadResponseAsync_RejectsUnexpectedPayloadType()
    {
        await using var stream = new MemoryStream();
        await WriteRawEnvelopeAsync(
            stream,
            new SessionPipeEnvelope<SessionControlResponse>(
                SessionControlProtocol.Version,
                SessionControlProtocol.RequestPayloadType,
                new SessionControlResponse(true, "ok")));
        stream.Position = 0;

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => PipeMessageProtocol.ReadResponseAsync(stream));

        Assert.Contains("Unexpected pipe payload type", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteRawEnvelopeAsync<T>(Stream stream, SessionPipeEnvelope<T> envelope)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, SessionGuardJson.Default);
        var lengthPrefix = BitConverter.GetBytes(payload.Length);
        await stream.WriteAsync(lengthPrefix);
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
    }
}
