using System.Text.Json;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Environment;
using SessionGuard.Infrastructure.Serialization;

namespace SessionGuard.Infrastructure.Services;

public sealed class FilePolicyApprovalStore : IPolicyApprovalStore
{
    private readonly string _approvalPath;
    private readonly IAppLogger _logger;

    public FilePolicyApprovalStore(RuntimePaths paths, IAppLogger logger)
    {
        _approvalPath = Path.Combine(paths.StateDirectory, "policy-approval.json");
        _logger = logger;
    }

    public async Task<PolicyApprovalState> GetCurrentAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var state = await LoadAsync(cancellationToken);
        if (!state.IsActive || !state.ExpiresAt.HasValue || state.ExpiresAt.Value <= now)
        {
            if (state.IsActive)
            {
                _logger.Info("policy.approval.expired", new { expiredAt = state.ExpiresAt });
            }

            await ClearAsync(cancellationToken);
            return PolicyApprovalState.None;
        }

        return state;
    }

    public async Task<PolicyApprovalState> GrantAsync(
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedDuration = duration < TimeSpan.FromMinutes(5)
            ? TimeSpan.FromMinutes(5)
            : duration > TimeSpan.FromHours(8)
                ? TimeSpan.FromHours(8)
                : duration;
        var state = new PolicyApprovalState(
            IsActive: true,
            GrantedAt: now,
            ExpiresAt: now.Add(normalizedDuration),
            WindowMinutes: (int)Math.Round(normalizedDuration.TotalMinutes));

        await SaveAsync(state, cancellationToken);
        _logger.Info("policy.approval.granted", new { state.ExpiresAt, state.WindowMinutes });
        return state;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(_approvalPath))
        {
            File.Delete(_approvalPath);
            _logger.Info("policy.approval.cleared");
        }

        return Task.CompletedTask;
    }

    private async Task<PolicyApprovalState> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_approvalPath))
        {
            return PolicyApprovalState.None;
        }

        await using var stream = File.OpenRead(_approvalPath);
        return await JsonSerializer.DeserializeAsync<PolicyApprovalState>(
                   stream,
                   SessionGuardJson.Default,
                   cancellationToken) ??
               PolicyApprovalState.None;
    }

    private async Task SaveAsync(PolicyApprovalState state, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(_approvalPath);
        await JsonSerializer.SerializeAsync(
            stream,
            state,
            SessionGuardJson.Indented,
            cancellationToken);
    }
}
