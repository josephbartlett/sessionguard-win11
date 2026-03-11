using SessionGuard.Core.Configuration;
using SessionGuard.Core.Models;

namespace SessionGuard.Core.Services;

public static class PolicyConfigurationValidator
{
    public static PolicyValidationReport Validate(
        PolicyConfiguration configuration,
        string? sourcePath = null)
    {
        var normalizedConfiguration = configuration.Normalize();
        var issues = new List<PolicyValidationIssue>();

        if (!normalizedConfiguration.Enabled)
        {
            issues.Add(new PolicyValidationIssue(
                "policy-engine-disabled",
                PolicyValidationSeverity.Information,
                BuildSourceMessage(sourcePath, "Policy evaluation is disabled by configuration.")));
        }

        var disabledRuleCount = normalizedConfiguration.Rules.Count(rule => !rule.Enabled);
        if (disabledRuleCount > 0)
        {
            issues.Add(new PolicyValidationIssue(
                "policy-rules-disabled",
                PolicyValidationSeverity.Information,
                $"{disabledRuleCount} rule(s) are disabled and will be skipped during evaluation."));
        }

        var duplicateRuleIds = normalizedConfiguration.Rules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Id))
            .GroupBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var duplicateRuleId in duplicateRuleIds)
        {
            issues.Add(new PolicyValidationIssue(
                "duplicate-rule-id",
                PolicyValidationSeverity.Error,
                $"Rule id '{duplicateRuleId.Key}' is duplicated {duplicateRuleId.Count()} time(s). Rule ids must stay unique for clear diagnostics and rule ownership.",
                duplicateRuleId.Key));
        }

        foreach (var rule in normalizedConfiguration.Rules.Where(rule => rule.Enabled))
        {
            switch (rule.Kind)
            {
                case PolicyRuleKind.ProcessBlock when rule.ProcessNames.Count == 0:
                    issues.Add(new PolicyValidationIssue(
                        "empty-process-block",
                        PolicyValidationSeverity.Warning,
                        "Enabled process block rule has no processNames configured, so it can never match.",
                        rule.Id));
                    break;
                case PolicyRuleKind.WorkspaceCategoryBlock when rule.WorkspaceCategories.Count == 0:
                    issues.Add(new PolicyValidationIssue(
                        "empty-workspace-block",
                        PolicyValidationSeverity.Warning,
                        "Enabled workspace block rule has no workspaceCategories configured, so it can never match.",
                        rule.Id));
                    break;
                case PolicyRuleKind.RestartWindow when rule.Days.Count == 0 && rule.StartHour == rule.EndHour:
                    issues.Add(new PolicyValidationIssue(
                        "restart-window-always-open",
                        PolicyValidationSeverity.Warning,
                        "Restart window rule currently allows every hour of every day, so it does not constrain restart timing.",
                        rule.Id));
                    break;
            }
        }

        var enabledRestartWindowRules = normalizedConfiguration.Rules
            .Where(rule => rule.Enabled && rule.Kind == PolicyRuleKind.RestartWindow)
            .ToArray();
        if (enabledRestartWindowRules.Length > 1)
        {
            issues.Add(new PolicyValidationIssue(
                "multiple-restart-windows",
                PolicyValidationSeverity.Warning,
                "Multiple restart window rules are enabled. SessionGuard evaluates them independently, which effectively intersects allowed windows and can block more often than expected."));
        }

        var enabledApprovalRuleWindows = normalizedConfiguration.Rules
            .Where(rule => rule.Enabled && rule.Kind == PolicyRuleKind.ApprovalRequired)
            .Select(rule => rule.ApprovalWindowMinutes ?? normalizedConfiguration.DefaultApprovalWindowMinutes)
            .Distinct()
            .ToArray();
        if (enabledApprovalRuleWindows.Length > 1)
        {
            issues.Add(new PolicyValidationIssue(
                "multiple-approval-windows",
                PolicyValidationSeverity.Warning,
                "Approval-required rules declare different approval window lengths. SessionGuard uses the highest-precedence matching approval rule to choose the restart approval duration."));
        }

        if (normalizedConfiguration.Enabled &&
            normalizedConfiguration.Rules.Count(rule => rule.Enabled) == 0)
        {
            issues.Add(new PolicyValidationIssue(
                "no-enabled-rules",
                PolicyValidationSeverity.Information,
                "Policy evaluation is enabled, but there are no active rules configured."));
        }

        return PolicyValidationReport.Create(issues);
    }

    public static PolicyValidationReport BuildLoadFailure(string sourcePath, string errorMessage)
    {
        return PolicyValidationReport.Create(
            new[]
            {
                new PolicyValidationIssue(
                    "policy-load-failed",
                    PolicyValidationSeverity.Error,
                    BuildSourceMessage(
                        sourcePath,
                        $"SessionGuard could not parse the policy file. Policy evaluation is disabled until the file is fixed. Parser message: {errorMessage}"))
            });
    }

    private static string BuildSourceMessage(string? sourcePath, string message)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return message;
        }

        return $"{Path.GetFileName(sourcePath)}: {message}";
    }
}
