namespace SessionGuard.Core.Models;

public sealed record WorkspaceProcessObservation(
    IReadOnlyList<ProtectedProcessMatch> ProtectedProcesses,
    IReadOnlyList<ObservedProcessInfo> RunningProcesses);
