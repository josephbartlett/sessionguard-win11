namespace SessionGuard.Core.Models;

public sealed record MitigationCommandResult(
    bool Success,
    bool RequiresElevation,
    bool RequiresService,
    string Message,
    IReadOnlyList<ManagedMitigationState> CurrentStates);
