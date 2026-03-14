using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Environment;

namespace SessionGuard.Service;

public sealed class SessionGuardRuntimeAccessPolicy
{
    private readonly RuntimePaths _paths;
    private readonly IAppLogger _logger;

    public SessionGuardRuntimeAccessPolicy(
        RuntimePaths paths,
        IAppLogger logger,
        string? processUserSidOverride = null,
        bool? processIsLocalSystemOverride = null)
    {
        _paths = paths;
        _logger = logger;
        ManifestPath = Path.Combine(paths.RepositoryRoot, "install-manifest.json");

        var processUserSid = string.IsNullOrWhiteSpace(processUserSidOverride)
            ? WindowsIdentity.GetCurrent().User?.Value
            : processUserSidOverride;
        var isLocalSystem = processIsLocalSystemOverride ?? IsLocalSystemSid(processUserSid);
        var manifestAuthorizedSid = TryReadAuthorizedUserSid(ManifestPath);

        if (!string.IsNullOrWhiteSpace(manifestAuthorizedSid))
        {
            AuthorizedUserSid = manifestAuthorizedSid;
            HasExplicitAuthorizedUser = true;
            UsesAuthenticatedUserFallback = false;
            return;
        }

        if (!isLocalSystem && !string.IsNullOrWhiteSpace(processUserSid))
        {
            AuthorizedUserSid = processUserSid;
            HasExplicitAuthorizedUser = false;
            UsesAuthenticatedUserFallback = false;
            return;
        }

        AuthorizedUserSid = null;
        HasExplicitAuthorizedUser = false;
        UsesAuthenticatedUserFallback = true;
    }

    public string ManifestPath { get; }

    public string? AuthorizedUserSid { get; }

    public bool HasExplicitAuthorizedUser { get; }

    public bool UsesAuthenticatedUserFallback { get; }

    public PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();

        if (!string.IsNullOrWhiteSpace(AuthorizedUserSid))
        {
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(AuthorizedUserSid),
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));
        }
        else if (UsesAuthenticatedUserFallback)
        {
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));
        }

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return security;
    }

    public void EnsureSensitiveDirectoryAccess()
    {
        if (!HasExplicitAuthorizedUser || string.IsNullOrWhiteSpace(AuthorizedUserSid))
        {
            return;
        }

        try
        {
            ApplyDirectoryAcl(_paths.LogDirectory, AuthorizedUserSid);
            ApplyDirectoryAcl(_paths.StateDirectory, AuthorizedUserSid);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or InvalidOperationException or PlatformNotSupportedException)
        {
            _logger.Warn(
                "service.security.runtime_acl.unavailable",
                new
                {
                    exception.Message,
                    authorizedUserSid = AuthorizedUserSid
                });
        }
    }

    internal static string? TryReadAuthorizedUserSid(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(manifestPath);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("AuthorizedUserSid", out var sidElement))
            {
                return null;
            }

            var sid = sidElement.GetString();
            if (string.IsNullOrWhiteSpace(sid))
            {
                return null;
            }

            _ = new SecurityIdentifier(sid);
            return sid;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsLocalSystemSid(string? sid)
    {
        return string.Equals(
            sid,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value,
            StringComparison.Ordinal);
    }

    private static void ApplyDirectoryAcl(string directoryPath, string authorizedUserSid)
    {
        Directory.CreateDirectory(directoryPath);

        var directorySecurity = BuildDirectorySecurity(authorizedUserSid);
        new DirectoryInfo(directoryPath).SetAccessControl(directorySecurity);

        foreach (var subdirectory in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.AllDirectories))
        {
            new DirectoryInfo(subdirectory).SetAccessControl(BuildDirectorySecurity(authorizedUserSid));
        }

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            new FileInfo(file).SetAccessControl(BuildFileSecurity(authorizedUserSid));
        }
    }

    private static DirectorySecurity BuildDirectorySecurity(string authorizedUserSid)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(authorizedUserSid),
            FileSystemRights.Modify | FileSystemRights.Synchronize,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        return security;
    }

    private static FileSecurity BuildFileSecurity(string authorizedUserSid)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(authorizedUserSid),
            FileSystemRights.Modify | FileSystemRights.Synchronize,
            AccessControlType.Allow));
        return security;
    }
}
