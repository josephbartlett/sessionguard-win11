using System.Text.Json;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Environment;
using SessionGuard.Infrastructure.Ipc;
using SessionGuard.Infrastructure.Serialization;

namespace SessionGuard.Service;

public sealed class SessionGuardServiceHealthReporter
{
    private readonly RuntimePaths _paths;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _healthPath;

    public SessionGuardServiceHealthReporter(RuntimePaths paths, IAppLogger logger)
    {
        _paths = paths;
        _logger = logger;
        _healthPath = Path.Combine(_paths.StateDirectory, "service-health.json");
    }

    public string HealthPath => _healthPath;

    public Task InitializeAsync(string hostMode, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        return UpdateAsync(
            snapshot =>
            {
                var current = snapshot ?? CreateSnapshot(hostMode, now);
                return current with
                {
                    HostMode = hostMode,
                    HealthState = "Starting",
                    LastUpdatedAt = now,
                    LastStartedAt = now,
                    LastStoppedAt = null,
                    PipeServerListening = false,
                    PipeServerStartedAt = null,
                    LastErrorStage = null,
                    LastErrorAt = null,
                    LastErrorMessage = null
                };
            },
            cancellationToken);
    }

    public Task RecordPipeServerStartedAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        return UpdateAsync(
            snapshot => (snapshot ?? CreateSnapshot("Unknown", now)) with
            {
                HealthState = DetermineHealthState(snapshot?.LastSuccessfulScanAt, hasError: snapshot?.LastErrorAt is not null),
                PipeServerListening = true,
                PipeServerStartedAt = now,
                LastUpdatedAt = now
            },
            cancellationToken);
    }

    public Task RecordScanAsync(
        SessionControlStatus status,
        int scanIntervalSeconds,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        return UpdateAsync(
            snapshot => (snapshot ?? CreateSnapshot("Unknown", now)) with
            {
                HealthState = "Running",
                LastUpdatedAt = now,
                LastSuccessfulScanAt = status.ScanResult.Timestamp,
                ScanIntervalSeconds = scanIntervalSeconds,
                LastScanState = status.ScanResult.State,
                LastScanRiskLevel = status.ScanResult.RiskLevel,
                LastScanSummary = status.ScanResult.Summary,
                LastGuardModeEnabled = status.GuardModeEnabled,
                ApprovalWindowActive = status.ScanResult.Policy.ApprovalActive,
                ApprovalWindowExpiresAt = status.ScanResult.Policy.ApprovalExpiresAt,
                ApprovalWindowMinutes = status.ScanResult.Policy.RecommendedApprovalWindowMinutes,
                ApprovalStateSummary = status.ScanResult.Policy.ApprovalActive && status.ScanResult.Policy.ApprovalExpiresAt.HasValue
                    ? $"Temporary approval active until {status.ScanResult.Policy.ApprovalExpiresAt.Value.LocalDateTime:G}."
                    : "No temporary approval window is active.",
                LastErrorStage = null,
                LastErrorAt = null,
                LastErrorMessage = null
            },
            cancellationToken);
    }

    public Task RecordApprovalRecoveryAsync(
        PolicyApprovalState approvalState,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        return UpdateAsync(
            snapshot => (snapshot ?? CreateSnapshot("Unknown", now)) with
            {
                LastUpdatedAt = now,
                ApprovalWindowActive = approvalState.IsActive,
                ApprovalWindowExpiresAt = approvalState.ExpiresAt,
                ApprovalWindowMinutes = approvalState.WindowMinutes,
                ApprovalStateRecoveredAt = now,
                ApprovalStateSummary = approvalState.IsActive && approvalState.ExpiresAt.HasValue
                    ? $"Recovered a persisted temporary approval window that remains active until {approvalState.ExpiresAt.Value.LocalDateTime:G}."
                    : "No persisted temporary approval window was active during service startup."
            },
            cancellationToken);
    }

    public Task RecordErrorAsync(
        string stage,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        return UpdateAsync(
            snapshot => (snapshot ?? CreateSnapshot("Unknown", now)) with
            {
                HealthState = "Degraded",
                LastUpdatedAt = now,
                LastErrorStage = stage,
                LastErrorAt = now,
                LastErrorMessage = exception.Message
            },
            cancellationToken);
    }

    public Task RecordStoppedAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        return UpdateAsync(
            snapshot => (snapshot ?? CreateSnapshot("Unknown", now)) with
            {
                HealthState = "Stopped",
                LastUpdatedAt = now,
                LastStoppedAt = now,
                PipeServerListening = false
            },
            cancellationToken);
    }

    private async Task UpdateAsync(
        Func<ServiceHealthSnapshot?, ServiceHealthSnapshot> update,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var current = await LoadSnapshotLockedAsync(cancellationToken);
            var normalizedCurrent = current is null ? null : NormalizeSnapshot(current);
            var next = NormalizeSnapshot(update(normalizedCurrent));

            await using var stream = File.Create(_healthPath);
            await JsonSerializer.SerializeAsync(stream, next, SessionGuardJson.Indented, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Warn(
                "service.health.persist.failed",
                new
                {
                    path = _healthPath,
                    exception = exception.Message
                });
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ServiceHealthSnapshot?> LoadSnapshotLockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_healthPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_healthPath);
        return await JsonSerializer.DeserializeAsync<ServiceHealthSnapshot>(stream, SessionGuardJson.Default, cancellationToken);
    }

    private ServiceHealthSnapshot CreateSnapshot(string hostMode, DateTimeOffset now)
    {
        return new ServiceHealthSnapshot(
            SessionGuardServiceMetadata.ServiceName,
            SessionGuardServiceMetadata.DisplayName,
            ServiceVersionInfo.ResolveProductVersion(),
            hostMode,
            "Starting",
            SessionControlProtocol.Version,
            Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SessionGuard.Service.exe"),
            AppContext.BaseDirectory,
            _paths.RepositoryRoot,
            _paths.ConfigDirectory,
            _paths.LogDirectory,
            _paths.StateDirectory,
            _healthPath,
            PipeServerListening: false,
            LastUpdatedAt: now,
            LastStartedAt: now);
    }

    private ServiceHealthSnapshot NormalizeSnapshot(ServiceHealthSnapshot snapshot)
    {
        return snapshot with
        {
            ServiceName = SessionGuardServiceMetadata.ServiceName,
            DisplayName = SessionGuardServiceMetadata.DisplayName,
            ProductVersion = ServiceVersionInfo.ResolveProductVersion(),
            ProtocolVersion = SessionControlProtocol.Version,
            ExecutablePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SessionGuard.Service.exe"),
            BaseDirectory = AppContext.BaseDirectory,
            RepositoryRoot = _paths.RepositoryRoot,
            ConfigDirectory = _paths.ConfigDirectory,
            LogDirectory = _paths.LogDirectory,
            StateDirectory = _paths.StateDirectory,
            HealthFilePath = _healthPath
        };
    }

    private static string DetermineHealthState(DateTimeOffset? lastSuccessfulScanAt, bool hasError)
    {
        if (hasError)
        {
            return "Degraded";
        }

        return lastSuccessfulScanAt.HasValue ? "Running" : "Starting";
    }
}
