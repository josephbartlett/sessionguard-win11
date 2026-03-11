using SessionGuard.Core.Configuration;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;

namespace SessionGuard.Tests;

public sealed class PolicyEvaluatorTests
{
    [Fact]
    public void Evaluate_ReturnsBlocked_WhenProcessRuleMatches()
    {
        var configuration = new PolicyConfiguration
        {
            Rules = new[]
            {
                new PolicyRuleDefinition
                {
                    Id = "block-terminal-sessions",
                    Title = "Never restart while terminals are running",
                    Kind = PolicyRuleKind.ProcessBlock,
                    ProcessNames = new[] { "pwsh.exe", "WindowsTerminal.exe" },
                    MinimumInstances = 1
                }
            }
        }.Normalize();

        var evaluation = PolicyEvaluator.Evaluate(
            configuration,
            PolicyApprovalState.None,
            CreateContext());

        Assert.Equal(PolicyDecisionType.RestartBlocked, evaluation.Decision);
        Assert.True(evaluation.HasBlockingRules);
        Assert.Contains(evaluation.MatchedRules, rule => rule.RuleId == "block-terminal-sessions");
    }

    [Fact]
    public void Evaluate_ReturnsApprovalRequired_WhenRiskThresholdMatchesWithoutApproval()
    {
        var configuration = new PolicyConfiguration
        {
            DefaultApprovalWindowMinutes = 75,
            Rules = new[]
            {
                new PolicyRuleDefinition
                {
                    Id = "approval-required-restart-pending",
                    Title = "Approval required for restart-pending states",
                    Kind = PolicyRuleKind.ApprovalRequired,
                    MatchWhenRestartPendingOnly = true,
                    MinimumRiskLevel = RestartRiskLevel.Elevated,
                    ApprovalWindowMinutes = 75
                }
            }
        }.Normalize();

        var evaluation = PolicyEvaluator.Evaluate(
            configuration,
            PolicyApprovalState.None,
            CreateContext(restartPending: true, riskLevel: RestartRiskLevel.Elevated));

        Assert.Equal(PolicyDecisionType.ApprovalRequired, evaluation.Decision);
        Assert.True(evaluation.RequiresApproval);
        Assert.Equal(75, evaluation.RecommendedApprovalWindowMinutes);
    }

    [Fact]
    public void Evaluate_ReturnsApprovalActive_WhenApprovalWindowExists()
    {
        var timestamp = DateTimeOffset.Parse("2026-03-11T15:00:00-04:00");
        var configuration = new PolicyConfiguration
        {
            Rules = new[]
            {
                new PolicyRuleDefinition
                {
                    Id = "approval-required-restart-pending",
                    Title = "Approval required for restart-pending states",
                    Kind = PolicyRuleKind.ApprovalRequired,
                    MatchWhenRestartPendingOnly = true,
                    MinimumRiskLevel = RestartRiskLevel.Elevated
                }
            }
        }.Normalize();

        var evaluation = PolicyEvaluator.Evaluate(
            configuration,
            new PolicyApprovalState(true, timestamp, timestamp.AddMinutes(60), 60),
            CreateContext(timestamp, restartPending: true, riskLevel: RestartRiskLevel.High));

        Assert.Equal(PolicyDecisionType.ApprovalActive, evaluation.Decision);
        Assert.True(evaluation.ApprovalActive);
        Assert.Contains("active until", evaluation.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_UsesHighestPrecedenceApprovalWindow_WhenMultipleApprovalRulesMatch()
    {
        var configuration = new PolicyConfiguration
        {
            DefaultApprovalWindowMinutes = 60,
            Rules = new[]
            {
                new PolicyRuleDefinition
                {
                    Id = "approval-high-precedence",
                    Title = "Short approval",
                    Kind = PolicyRuleKind.ApprovalRequired,
                    Priority = 10,
                    MatchWhenRestartPendingOnly = true,
                    MinimumRiskLevel = RestartRiskLevel.Elevated,
                    ApprovalWindowMinutes = 45
                },
                new PolicyRuleDefinition
                {
                    Id = "approval-low-precedence",
                    Title = "Long approval",
                    Kind = PolicyRuleKind.ApprovalRequired,
                    Priority = 20,
                    MatchWhenRestartPendingOnly = true,
                    MinimumRiskLevel = RestartRiskLevel.Elevated,
                    ApprovalWindowMinutes = 90
                }
            }
        }.Normalize();

        var evaluation = PolicyEvaluator.Evaluate(
            configuration,
            PolicyApprovalState.None,
            CreateContext(restartPending: true, riskLevel: RestartRiskLevel.High));

        Assert.Equal(PolicyDecisionType.ApprovalRequired, evaluation.Decision);
        Assert.Equal(45, evaluation.RecommendedApprovalWindowMinutes);
        Assert.Contains(evaluation.EvaluationTrace, trace => trace.Contains("Approval window conflict", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_ExplainsBlockingRulesOverrideApprovalRules()
    {
        var configuration = new PolicyConfiguration
        {
            Rules = new[]
            {
                new PolicyRuleDefinition
                {
                    Id = "block-terminal-sessions",
                    Title = "Never restart while terminals are running",
                    Kind = PolicyRuleKind.ProcessBlock,
                    ProcessNames = new[] { "pwsh.exe" },
                    MinimumInstances = 1
                },
                new PolicyRuleDefinition
                {
                    Id = "approval-required-high-risk",
                    Title = "Approval required for high risk",
                    Kind = PolicyRuleKind.ApprovalRequired,
                    MinimumRiskLevel = RestartRiskLevel.Elevated,
                    ApprovalWindowMinutes = 45
                }
            }
        }.Normalize();

        var evaluation = PolicyEvaluator.Evaluate(
            configuration,
            PolicyApprovalState.None,
            CreateContext(restartPending: false, riskLevel: RestartRiskLevel.High));

        Assert.Equal(PolicyDecisionType.RestartBlocked, evaluation.Decision);
        Assert.Contains(evaluation.EvaluationTrace, trace => trace.Contains("take precedence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_SurfacesValidationErrors_WhenConfigurationLoadFailed()
    {
        var configuration = new PolicyConfiguration
        {
            Enabled = false
        }.Normalize();

        var validation = PolicyConfigurationValidator.BuildLoadFailure(
            "config\\policies.json",
            "Expected StartObject token.");
        var evaluation = PolicyEvaluator.Evaluate(
            configuration,
            PolicyApprovalState.None,
            CreateContext(),
            validation);

        Assert.Equal(PolicyDecisionType.None, evaluation.Decision);
        Assert.True(evaluation.Validation.HasErrors);
        Assert.Contains("unavailable", evaluation.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(evaluation.EvaluationTrace, trace => trace.Contains("policy-load-failed", StringComparison.OrdinalIgnoreCase) ||
                                                             trace.Contains("policies.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Normalize_SortsRulesDeterministically()
    {
        var configuration = new PolicyConfiguration
        {
            Rules = new[]
            {
                new PolicyRuleDefinition
                {
                    Id = "z-rule",
                    Title = "Zulu rule",
                    Kind = PolicyRuleKind.ProcessBlock,
                    Priority = 50
                },
                new PolicyRuleDefinition
                {
                    Id = "a-rule",
                    Title = "Alpha rule",
                    Kind = PolicyRuleKind.ProcessBlock,
                    Priority = 10
                }
            }
        }.Normalize();

        Assert.Equal(new[] { "a-rule", "z-rule" }, configuration.Rules.Select(rule => rule.Id));
    }

    private static PolicyEvaluationContext CreateContext(
        DateTimeOffset? timestamp = null,
        bool restartPending = false,
        RestartRiskLevel riskLevel = RestartRiskLevel.High)
    {
        var effectiveTimestamp = timestamp ?? DateTimeOffset.Parse("2026-03-11T15:00:00-04:00");
        return new PolicyEvaluationContext(
            effectiveTimestamp,
            RestartStateCategory.ProtectedSessionActive,
            riskLevel,
            restartPending,
            new WorkspaceStateSnapshot(
                effectiveTimestamp,
                HasRisk: true,
                WorkspaceRiskSeverity.High,
                WorkspaceConfidence.High,
                "Workspace-risk heuristics flagged high-impact activity: Terminal and shell sessions.",
                new[]
                {
                    new WorkspaceRiskItem(
                        "Terminal and shell sessions",
                        WorkspaceCategory.TerminalShell,
                        WorkspaceRiskSeverity.High,
                        WorkspaceConfidence.High,
                        2,
                        "Interactive shells often hold live commands.",
                        new[] { "pwsh.exe", "WindowsTerminal.exe" })
                }),
            new[]
            {
                new ProtectedProcessMatch("pwsh.exe", 1),
                new ProtectedProcessMatch("WindowsTerminal.exe", 1)
            },
            new[]
            {
                new ObservedProcessInfo("pwsh.exe", 1),
                new ObservedProcessInfo("WindowsTerminal.exe", 1),
                new ObservedProcessInfo("chrome.exe", 1)
            });
    }
}
