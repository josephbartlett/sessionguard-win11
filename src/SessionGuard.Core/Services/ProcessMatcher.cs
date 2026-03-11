using SessionGuard.Core.Models;

namespace SessionGuard.Core.Services;

public static class ProcessMatcher
{
    public static IReadOnlyList<ProtectedProcessMatch> MatchProcesses(
        IEnumerable<string> configuredProcesses,
        IEnumerable<string> runningProcesses)
    {
        var configuredLookup = configuredProcesses
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => new
            {
                Key = NormalizeExecutableName(name),
                DisplayName = CanonicalizeDisplayName(name)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().DisplayName, StringComparer.OrdinalIgnoreCase);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var processName in runningProcesses)
        {
            var normalized = NormalizeExecutableName(processName);
            if (string.IsNullOrWhiteSpace(normalized) || !configuredLookup.ContainsKey(normalized))
            {
                continue;
            }

            counts[normalized] = counts.TryGetValue(normalized, out var currentCount) ? currentCount + 1 : 1;
        }

        return counts
            .OrderBy(pair => configuredLookup[pair.Key], StringComparer.OrdinalIgnoreCase)
            .Select(pair => new ProtectedProcessMatch(configuredLookup[pair.Key], pair.Value))
            .ToArray();
    }

    public static IReadOnlyList<ObservedProcessInfo> SummarizeProcesses(IEnumerable<string> runningProcesses)
    {
        return runningProcesses
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => new
            {
                Key = NormalizeExecutableName(name),
                DisplayName = CanonicalizeDisplayName(name)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ObservedProcessInfo(
                group.First().DisplayName,
                group.Count()))
            .ToArray();
    }

    public static string CanonicalizeDisplayName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var trimmed = name.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}.exe";
    }

    public static string NormalizeExecutableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var trimmed = name.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        return trimmed.ToUpperInvariant();
    }
}
