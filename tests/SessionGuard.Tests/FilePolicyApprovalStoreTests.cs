using SessionGuard.Core.Models;
using SessionGuard.Infrastructure.Environment;
using SessionGuard.Infrastructure.Services;
using SessionGuard.Core.Services;

namespace SessionGuard.Tests;

public sealed class FilePolicyApprovalStoreTests : IDisposable
{
    private readonly string _runtimeRoot;

    public FilePolicyApprovalStoreTests()
    {
        _runtimeRoot = Path.Combine(Path.GetTempPath(), "SessionGuard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_runtimeRoot);
        Directory.CreateDirectory(Path.Combine(_runtimeRoot, "config"));
    }

    [Fact]
    public async Task GrantAsync_PersistsApprovalWindow()
    {
        var store = CreateStore();
        var grantedAt = DateTimeOffset.Parse("2026-03-11T16:00:00-04:00");

        await store.GrantAsync(grantedAt, TimeSpan.FromMinutes(90));
        var current = await store.GetCurrentAsync(grantedAt.AddMinutes(5));

        Assert.True(current.IsActive);
        Assert.Equal(grantedAt.AddMinutes(90), current.ExpiresAt);
        Assert.Equal(90, current.WindowMinutes);
    }

    [Fact]
    public async Task GetCurrentAsync_ClearsExpiredApproval()
    {
        var store = CreateStore();
        var grantedAt = DateTimeOffset.Parse("2026-03-11T16:00:00-04:00");
        var paths = RuntimePaths.Discover(_runtimeRoot);

        await store.GrantAsync(grantedAt, TimeSpan.FromMinutes(30));
        var current = await store.GetCurrentAsync(grantedAt.AddMinutes(31));

        Assert.False(current.IsActive);
        Assert.False(File.Exists(Path.Combine(paths.StateDirectory, "policy-approval.json")));
    }

    [Fact]
    public async Task GetCurrentAsync_ClearsPersistedExpiredApprovalWindow()
    {
        var store = CreateStore();
        var paths = RuntimePaths.Discover(_runtimeRoot);
        var approvalPath = Path.Combine(paths.StateDirectory, "policy-approval.json");
        var expiredState = """
        {
          "isActive": true,
          "grantedAt": "2026-03-11T16:00:00-04:00",
          "expiresAt": "2026-03-11T16:30:00-04:00",
          "windowMinutes": 30
        }
        """;

        await File.WriteAllTextAsync(approvalPath, expiredState);

        var current = await store.GetCurrentAsync(DateTimeOffset.Parse("2026-03-11T16:31:00-04:00"));

        Assert.False(current.IsActive);
        Assert.False(File.Exists(approvalPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_runtimeRoot))
        {
            Directory.Delete(_runtimeRoot, recursive: true);
        }
    }

    private FilePolicyApprovalStore CreateStore()
    {
        return new FilePolicyApprovalStore(
            RuntimePaths.Discover(_runtimeRoot),
            new RecordingLogger());
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public string LogDirectory => "logs";

        public void Info(string message, object? context = null)
        {
        }

        public void Warn(string message, object? context = null)
        {
        }

        public void Error(string message, Exception exception, object? context = null)
        {
        }
    }
}
