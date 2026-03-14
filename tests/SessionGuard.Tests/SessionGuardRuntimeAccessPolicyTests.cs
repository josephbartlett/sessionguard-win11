using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Environment;
using SessionGuard.Service;

namespace SessionGuard.Tests;

public sealed class SessionGuardRuntimeAccessPolicyTests : IDisposable
{
    private readonly string _rootPath;

    public SessionGuardRuntimeAccessPolicyTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "SessionGuard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootPath);
    }

    [Fact]
    public void Constructor_UsesAuthorizedUserSidFromInstallManifest()
    {
        var authorizedSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null).Value;
        File.WriteAllText(
            Path.Combine(_rootPath, "install-manifest.json"),
            $$"""{"AuthorizedUserSid":"{{authorizedSid}}"}""");

        var policy = new SessionGuardRuntimeAccessPolicy(
            RuntimePaths.Discover(_rootPath),
            new RecordingLogger(),
            processUserSidOverride: new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null).Value,
            processIsLocalSystemOverride: true);

        Assert.Equal(authorizedSid, policy.AuthorizedUserSid);
        Assert.True(policy.HasExplicitAuthorizedUser);
        Assert.False(policy.UsesAuthenticatedUserFallback);

        var rules = policy.CreatePipeSecurity()
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .OfType<PipeAccessRule>()
            .ToArray();

        Assert.Contains(
            rules,
            rule => Equals(rule.IdentityReference, new SecurityIdentifier(authorizedSid)) &&
                    rule.PipeAccessRights.HasFlag(PipeAccessRights.ReadWrite) &&
                    !rule.PipeAccessRights.HasFlag(PipeAccessRights.CreateNewInstance));
        Assert.DoesNotContain(
            rules,
            rule => Equals(rule.IdentityReference, new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null)) &&
                    rule.PipeAccessRights.HasFlag(PipeAccessRights.CreateNewInstance));
    }

    [Fact]
    public void Constructor_FallsBackToAuthenticatedUsers_WhenRunningAsSystemWithoutManifest()
    {
        var policy = new SessionGuardRuntimeAccessPolicy(
            RuntimePaths.Discover(_rootPath),
            new RecordingLogger(),
            processUserSidOverride: new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value,
            processIsLocalSystemOverride: true);

        Assert.Null(policy.AuthorizedUserSid);
        Assert.False(policy.HasExplicitAuthorizedUser);
        Assert.True(policy.UsesAuthenticatedUserFallback);

        var rules = policy.CreatePipeSecurity()
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .OfType<PipeAccessRule>()
            .ToArray();

        Assert.Contains(
            rules,
            rule => Equals(rule.IdentityReference, new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null)) &&
                    rule.PipeAccessRights.HasFlag(PipeAccessRights.ReadWrite) &&
                    !rule.PipeAccessRights.HasFlag(PipeAccessRights.CreateNewInstance));
    }

    [Fact]
    public void EnsureSensitiveDirectoryAccess_ProtectsLogsAndStateForAuthorizedUser()
    {
        var authorizedSid = WindowsIdentity.GetCurrent().User!.Value;
        File.WriteAllText(
            Path.Combine(_rootPath, "install-manifest.json"),
            $$"""{"AuthorizedUserSid":"{{authorizedSid}}"}""");

        var logsDirectory = Path.Combine(_rootPath, "logs");
        var stateDirectory = Path.Combine(_rootPath, "state");
        Directory.CreateDirectory(logsDirectory);
        Directory.CreateDirectory(stateDirectory);
        File.WriteAllText(Path.Combine(logsDirectory, "app.log"), "test");
        File.WriteAllText(Path.Combine(stateDirectory, "current-scan.json"), "{}");

        var policy = new SessionGuardRuntimeAccessPolicy(
            RuntimePaths.Discover(_rootPath),
            new RecordingLogger());

        policy.EnsureSensitiveDirectoryAccess();

        var logRules = new DirectoryInfo(logsDirectory)
            .GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .OfType<FileSystemAccessRule>()
            .ToArray();

        Assert.Contains(
            logRules,
            rule => Equals(rule.IdentityReference, new SecurityIdentifier(authorizedSid)) &&
                    rule.FileSystemRights.HasFlag(FileSystemRights.Modify));
        Assert.DoesNotContain(
            logRules,
            rule => Equals(rule.IdentityReference, new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
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
