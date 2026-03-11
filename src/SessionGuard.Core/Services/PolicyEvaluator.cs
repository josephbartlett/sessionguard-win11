using SessionGuard.Core.Configuration;
using SessionGuard.Core.Models;

namespace SessionGuard.Core.Services;

public static class PolicyEvaluator
{
    public static PolicyEvaluation Evaluate(
        PolicyConfiguration configuration,
        PolicyApprovalState approvalState,
        PolicyEvaluationContext context)
    {
        var normalizedConfiguration = configuration.Normalize();
        if (!normalizedConfiguration.Enabled)
        {
            return PolicyEvaluation.None with
            {
                Summary = "Policy engine is disabled in config.",
                RecommendedApprovalWindowMinutes = normalizedConfiguration.DefaultApprovalWindowMinutes
            };
        }

        var enabledRules = normalizedConfiguration.Rules
            .Where(rule => rule.Enabled)
            .ToArray();
        if (enabledRules.Length == 0)
        {
            return PolicyEvaluation.None with
            {
                Summary = "No policy rules are currently enabled.",
                RecommendedApprovalWindowMinutes = normalizedConfiguration.DefaultApprovalWindowMinutes
            };
        }

        var matchedRules = new List<PolicyRuleMatch>();
        var traces = new List<string>();
        var approvalIsActive = approvalState.IsActive &&
                               approvalState.ExpiresAt.HasValue &&
                               approvalState.ExpiresAt.Value > context.Timestamp;
        var recommendedApprovalWindowMinutes = normalizedConfiguration.DefaultApprovalWindowMinutes;

        foreach (var rule in enabledRules)
        {
            var match = EvaluateRule(rule, context, approvalIsActive, approvalState, normalizedConfiguration);
            if (match is null)
            {
                continue;
            }

            matchedRules.Add(match);
            traces.Add($"{match.Title}: {match.Reason}");

            if (rule.Kind == PolicyRuleKind.ApprovalRequired)
            {
                recommendedApprovalWindowMinutes = rule.ApprovalWindowMinutes ??
                                                   normalizedConfiguration.DefaultApprovalWindowMinutes;
            }
        }

        if (matchedRules.Count == 0)
        {
            if (approvalIsActive)
            {
                return new PolicyEvaluation(
                    PolicyDecisionType.ApprovalActive,
                    HasBlockingRules: false,
                    RequiresApproval: false,
                    ApprovalActive: true,
                    approvalState.ExpiresAt,
                    normalizedConfiguration.DefaultApprovalWindowMinutes,
                    BuildSummary(PolicyDecisionType.ApprovalActive, Array.Empty<PolicyRuleMatch>(), approvalState),
                    Array.Empty<PolicyRuleMatch>(),
                    new[]
                    {
                        $"A temporary restart approval window is active until {approvalState.ExpiresAt!.Value.LocalDateTime:G}."
                    });
            }

            return PolicyEvaluation.None with
            {
                RecommendedApprovalWindowMinutes = normalizedConfiguration.DefaultApprovalWindowMinutes,
                Summary = "No policy rules are currently constraining restart behavior.",
                EvaluationTrace = new[]
                {
                    $"Policy engine evaluated {enabledRules.Length} enabled rule(s); none matched."
                }
            };
        }

        var hasBlockingRules = matchedRules.Any(match => match.Outcome == PolicyRuleOutcome.Blocked);
        var requiresApproval = matchedRules.Any(match => match.Outcome == PolicyRuleOutcome.ApprovalRequired);
        var matchedApprovalRule = matchedRules.Any(match =>
            match.Outcome is PolicyRuleOutcome.ApprovalRequired or PolicyRuleOutcome.Approved);

        var decision = DetermineDecision(hasBlockingRules, requiresApproval, approvalIsActive, matchedApprovalRule);
        var summary = BuildSummary(decision, matchedRules, approvalState);

        return new PolicyEvaluation(
            decision,
            hasBlockingRules,
            requiresApproval,
            approvalIsActive,
            approvalIsActive ? approvalState.ExpiresAt : null,
            recommendedApprovalWindowMinutes,
            summary,
            matchedRules,
            traces);
    }

    private static PolicyRuleMatch? EvaluateRule(
        PolicyRuleDefinition rule,
        PolicyEvaluationContext context,
        bool approvalIsActive,
        PolicyApprovalState approvalState,
        PolicyConfiguration configuration)
    {
        if (rule.MatchWhenRestartPendingOnly && !context.RestartPending)
        {
            return null;
        }

        return rule.Kind switch
        {
            PolicyRuleKind.RestartWindow => EvaluateRestartWindowRule(rule, context),
            PolicyRuleKind.ProcessBlock => EvaluateProcessBlockRule(rule, context),
            PolicyRuleKind.WorkspaceCategoryBlock => EvaluateWorkspaceCategoryRule(rule, context),
            PolicyRuleKind.ApprovalRequired => EvaluateApprovalRule(rule, context, approvalIsActive, approvalState, configuration),
            _ => null
        };
    }

    private static PolicyRuleMatch? EvaluateRestartWindowRule(
        PolicyRuleDefinition rule,
        PolicyEvaluationContext context)
    {
        var allowedDays = rule.Days.Count == 0
            ? Enum.GetValues<DayOfWeek>()
            : rule.Days;
        var inAllowedDay = allowedDays.Contains(context.Timestamp.DayOfWeek);
        var inAllowedHour = IsHourWithinWindow(context.Timestamp.Hour, rule.StartHour, rule.EndHour);

        if (inAllowedDay && inAllowedHour)
        {
            return null;
        }

        var dayCount = allowedDays.Count;
        var daySummary = dayCount == 7
            ? "every day"
            : string.Join(", ", allowedDays.Select(static day => day.ToString()));
        var reason =
            $"Current local time {context.Timestamp.LocalDateTime:dddd HH:mm} is outside the allowed restart window ({daySummary}, {rule.StartHour:00}:00-{rule.EndHour:00}:00).";

        return new PolicyRuleMatch(
            rule.Id,
            rule.Title,
            rule.Kind,
            PolicyRuleOutcome.Blocked,
            rule.Priority,
            reason);
    }

    private static PolicyRuleMatch? EvaluateProcessBlockRule(
        PolicyRuleDefinition rule,
        PolicyEvaluationContext context)
    {
        if (rule.ProcessNames.Count == 0)
        {
            return null;
        }

        var configured = rule.ProcessNames
            .Select(ProcessMatcher.NormalizeExecutableName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = context.ObservedProcesses
            .Where(process => configured.Contains(ProcessMatcher.NormalizeExecutableName(process.DisplayName)))
            .OrderBy(process => process.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var totalInstances = matches.Sum(process => process.InstanceCount);

        if (totalInstances < rule.MinimumInstances)
        {
            return null;
        }

        var matchedProcesses = string.Join(", ", matches.Select(process => $"{process.DisplayName} x{process.InstanceCount}"));
        var reason = $"{totalInstances} matching process instance(s) detected: {matchedProcesses}.";

        return new PolicyRuleMatch(
            rule.Id,
            rule.Title,
            rule.Kind,
            PolicyRuleOutcome.Blocked,
            rule.Priority,
            reason);
    }

    private static PolicyRuleMatch? EvaluateWorkspaceCategoryRule(
        PolicyRuleDefinition rule,
        PolicyEvaluationContext context)
    {
        if (rule.WorkspaceCategories.Count == 0)
        {
            return null;
        }

        var matchingItems = context.Workspace.RiskItems
            .Where(item => rule.WorkspaceCategories.Contains(item.Category))
            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var totalInstances = matchingItems.Sum(item => item.InstanceCount);

        if (totalInstances < rule.MinimumInstances)
        {
            return null;
        }

        var groups = string.Join(", ", matchingItems.Select(item => $"{item.Title} x{item.InstanceCount}"));
        var reason = $"{totalInstances} matching workspace instance(s) detected across {groups}.";

        return new PolicyRuleMatch(
            rule.Id,
            rule.Title,
            rule.Kind,
            PolicyRuleOutcome.Blocked,
            rule.Priority,
            reason);
    }

    private static PolicyRuleMatch? EvaluateApprovalRule(
        PolicyRuleDefinition rule,
        PolicyEvaluationContext context,
        bool approvalIsActive,
        PolicyApprovalState approvalState,
        PolicyConfiguration configuration)
    {
        if (CompareRiskLevel(context.RiskLevel, rule.MinimumRiskLevel) < 0)
        {
            return null;
        }

        var windowMinutes = rule.ApprovalWindowMinutes ?? configuration.DefaultApprovalWindowMinutes;
        if (approvalIsActive && approvalState.ExpiresAt.HasValue)
        {
            return new PolicyRuleMatch(
                rule.Id,
                rule.Title,
                rule.Kind,
                PolicyRuleOutcome.Approved,
                rule.Priority,
                $"A temporary restart approval window is active until {approvalState.ExpiresAt.Value.LocalDateTime:G}.");
        }

        return new PolicyRuleMatch(
            rule.Id,
            rule.Title,
            rule.Kind,
            PolicyRuleOutcome.ApprovalRequired,
            rule.Priority,
            $"Risk level {context.RiskLevel} meets the approval threshold {rule.MinimumRiskLevel}. Grant a supervised approval window for {windowMinutes} minute(s) before restarting.");
    }

    private static PolicyDecisionType DetermineDecision(
        bool hasBlockingRules,
        bool requiresApproval,
        bool approvalIsActive,
        bool matchedApprovalRule)
    {
        if (hasBlockingRules)
        {
            return PolicyDecisionType.RestartBlocked;
        }

        if (requiresApproval)
        {
            return PolicyDecisionType.ApprovalRequired;
        }

        if (approvalIsActive && matchedApprovalRule)
        {
            return PolicyDecisionType.ApprovalActive;
        }

        return PolicyDecisionType.None;
    }

    private static string BuildSummary(
        PolicyDecisionType decision,
        IReadOnlyList<PolicyRuleMatch> matches,
        PolicyApprovalState approvalState)
    {
        var blockingTitles = matches
            .Where(match => match.Outcome == PolicyRuleOutcome.Blocked)
            .Select(match => match.Title)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var approvalTitles = matches
            .Where(match => match.Outcome is PolicyRuleOutcome.ApprovalRequired or PolicyRuleOutcome.Approved)
            .Select(match => match.Title)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return decision switch
        {
            PolicyDecisionType.RestartBlocked when approvalState.IsActive && approvalState.ExpiresAt.HasValue =>
                $"Policy rules are blocking restart right now: {string.Join(", ", blockingTitles)}. An approval window is active until {approvalState.ExpiresAt.Value.LocalDateTime:G}, but it does not override blocking rules.",
            PolicyDecisionType.RestartBlocked =>
                $"Policy rules are blocking restart right now: {string.Join(", ", blockingTitles)}.",
            PolicyDecisionType.ApprovalRequired =>
                $"Policy rules require a temporary approval window before a supervised restart: {string.Join(", ", approvalTitles)}.",
            PolicyDecisionType.ApprovalActive when approvalState.ExpiresAt.HasValue =>
                $"A temporary policy approval window is active until {approvalState.ExpiresAt.Value.LocalDateTime:G}.",
            _ =>
                "No policy rules are currently constraining restart behavior."
        };
    }

    private static bool IsHourWithinWindow(int hour, int startHour, int endHour)
    {
        if (startHour == endHour)
        {
            return true;
        }

        return startHour < endHour
            ? hour >= startHour && hour < endHour
            : hour >= startHour || hour < endHour;
    }

    private static int CompareRiskLevel(RestartRiskLevel left, RestartRiskLevel right)
    {
        return RiskRank(left).CompareTo(RiskRank(right));
    }

    private static int RiskRank(RestartRiskLevel riskLevel)
    {
        return riskLevel switch
        {
            RestartRiskLevel.Low => 0,
            RestartRiskLevel.Elevated => 1,
            RestartRiskLevel.High => 2,
            RestartRiskLevel.Unknown => -1,
            _ => -1
        };
    }
}
