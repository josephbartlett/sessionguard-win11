using SessionGuard.Core.Models;

namespace SessionGuard.Core.Automation;

public sealed record UiSmokeScenario(
    string Name,
    string Description,
    SessionControlStatus Status,
    IReadOnlyDictionary<string, string> ExpectedTexts);
