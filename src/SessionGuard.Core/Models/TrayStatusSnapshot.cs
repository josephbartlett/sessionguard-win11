namespace SessionGuard.Core.Models;

public sealed record TrayStatusSnapshot(
    string TooltipText,
    string SummaryLine,
    string NextStepLine,
    string ContextLine,
    string StatusLine,
    string ModeLine,
    string PolicyLine,
    string TimingLine);
