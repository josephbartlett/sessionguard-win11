namespace SessionGuard.Core.Models;

public sealed record TrayStatusSnapshot(
    string TooltipText,
    string StatusLine,
    string ModeLine,
    string PolicyLine,
    string TimingLine);
