namespace SessionGuard.Infrastructure.Configuration;

public enum ConfigurationUpgradeStatus
{
    Current,
    NeedsUpgrade,
    Upgraded,
    Missing,
    UnsupportedFutureVersion,
    InvalidJson,
    WriteFailed
}
