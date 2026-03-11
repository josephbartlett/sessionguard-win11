using SessionGuard.Core.Configuration;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;

namespace SessionGuard.Tests;

public sealed class PolicyConfigurationValidatorTests
{
    [Fact]
    public void Validate_ReportsDisabledRulesAndConflictingWindows()
    {
        var configuration = new PolicyConfiguration
        {
            Rules = new[]
            {
                new PolicyRuleDefinition
                {
                    Id = "weekend-window",
                    Title = "Weekend window",
                    Enabled = true,
                    Priority = 10,
                    Kind = PolicyRuleKind.RestartWindow,
                    Days = new[] { DayOfWeek.Saturday, DayOfWeek.Sunday },
                    StartHour = 9,
                    EndHour = 17
                },
                new PolicyRuleDefinition
                {
                    Id = "night-window",
                    Title = "Night window",
                    Enabled = true,
                    Priority = 20,
                    Kind = PolicyRuleKind.RestartWindow,
                    Days = new[] { DayOfWeek.Saturday, DayOfWeek.Sunday },
                    StartHour = 0,
                    EndHour = 6
                },
                new PolicyRuleDefinition
                {
                    Id = "approval-low",
                    Title = "Approval 45",
                    Enabled = true,
                    Priority = 30,
                    Kind = PolicyRuleKind.ApprovalRequired,
                    ApprovalWindowMinutes = 45
                },
                new PolicyRuleDefinition
                {
                    Id = "approval-high",
                    Title = "Approval 90",
                    Enabled = true,
                    Priority = 40,
                    Kind = PolicyRuleKind.ApprovalRequired,
                    ApprovalWindowMinutes = 90
                },
                new PolicyRuleDefinition
                {
                    Id = "disabled-rule",
                    Title = "Disabled rule",
                    Enabled = false,
                    Priority = 50,
                    Kind = PolicyRuleKind.ProcessBlock,
                    ProcessNames = new[] { "pwsh.exe" }
                }
            }
        };

        var validation = PolicyConfigurationValidator.Validate(configuration, "config\\policies.json");

        Assert.False(validation.HasErrors);
        Assert.True(validation.HasWarnings);
        Assert.Contains(validation.Issues, issue => issue.Code == "policy-rules-disabled");
        Assert.Contains(validation.Issues, issue => issue.Code == "multiple-restart-windows");
        Assert.Contains(validation.Issues, issue => issue.Code == "multiple-approval-windows");
    }

    [Fact]
    public void Validate_ReportsDuplicateRuleIds()
    {
        var configuration = new PolicyConfiguration
        {
            Rules = new[]
            {
                new PolicyRuleDefinition
                {
                    Id = "duplicate-rule",
                    Title = "First rule",
                    Kind = PolicyRuleKind.ProcessBlock,
                    ProcessNames = new[] { "pwsh.exe" }
                },
                new PolicyRuleDefinition
                {
                    Id = "duplicate-rule",
                    Title = "Second rule",
                    Kind = PolicyRuleKind.ProcessBlock,
                    ProcessNames = new[] { "code.exe" }
                }
            }
        };

        var validation = PolicyConfigurationValidator.Validate(configuration);

        Assert.True(validation.HasErrors);
        Assert.Contains(validation.Issues, issue => issue.Code == "duplicate-rule-id");
    }
}
