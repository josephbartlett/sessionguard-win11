namespace SessionGuard.Core.Models;

public sealed record MitigationCommandResult(
    bool Success,
    bool RequiresElevation,
    string Message,
    IReadOnlyList<ManagedMitigationState> CurrentStates);
