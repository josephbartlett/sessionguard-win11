namespace SessionGuard.Core.Services;

public interface IAppLogger
{
    string LogDirectory { get; }

    void Info(string message, object? context = null);

    void Warn(string message, object? context = null);

    void Error(string message, Exception exception, object? context = null);
}
