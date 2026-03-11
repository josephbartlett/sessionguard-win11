using SessionGuard.Core.Services;

namespace SessionGuard.Core.Configuration;

public sealed class ProtectedProcessCatalog
{
    public IReadOnlyList<string> ProcessNames { get; init; } = Array.Empty<string>();

    public ProtectedProcessCatalog Normalize()
    {
        var normalized = ProcessNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(ProcessMatcher.CanonicalizeDisplayName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProtectedProcessCatalog
        {
            ProcessNames = normalized
        };
    }
}
