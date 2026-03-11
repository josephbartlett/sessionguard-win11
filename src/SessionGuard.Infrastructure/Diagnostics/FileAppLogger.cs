using System.Text;
using System.Text.Json;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Environment;

namespace SessionGuard.Infrastructure.Diagnostics;

public sealed class FileAppLogger : IAppLogger
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    private readonly object _syncRoot = new();
    private readonly RuntimePaths _paths;

    public FileAppLogger(RuntimePaths paths)
    {
        _paths = paths;
    }

    public string LogDirectory => _paths.LogDirectory;

    public void Info(string message, object? context = null)
    {
        Write("info", message, null, context);
    }

    public void Warn(string message, object? context = null)
    {
        Write("warn", message, null, context);
    }

    public void Error(string message, Exception exception, object? context = null)
    {
        Write("error", message, exception, context);
    }

    private void Write(string level, string message, Exception? exception, object? context)
    {
        var payload = new Dictionary<string, object?>
        {
            ["timestamp"] = DateTimeOffset.Now,
            ["level"] = level,
            ["message"] = message,
            ["context"] = context,
            ["exception"] = exception?.ToString()
        };

        var line = JsonSerializer.Serialize(payload, SerializerOptions) + System.Environment.NewLine;
        var filePath = Path.Combine(_paths.LogDirectory, $"sessionguard-{DateTime.Now:yyyyMMdd}.log");

        lock (_syncRoot)
        {
            File.AppendAllText(filePath, line, Encoding.UTF8);
        }
    }
}
