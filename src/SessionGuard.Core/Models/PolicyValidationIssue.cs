namespace SessionGuard.Core.Models;

public sealed record PolicyValidationIssue(
    string Code,
    PolicyValidationSeverity Severity,
    string Message,
    string? RuleId = null)
{
    public string SeverityLabel => Severity switch
    {
        PolicyValidationSeverity.Information => "Info",
        PolicyValidationSeverity.Warning => "Warning",
        PolicyValidationSeverity.Error => "Error",
        _ => Severity.ToString()
    };

    public string DisplayText => string.IsNullOrWhiteSpace(RuleId)
        ? Message
        : $"{RuleId}: {Message}";
}
