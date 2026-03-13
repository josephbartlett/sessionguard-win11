using System.IO;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;

namespace SessionGuard.App.Runtime;

internal sealed class AppInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateEvent;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listenerTask;
    private bool _disposed;

    public AppInstanceCoordinator(string? instanceKey = null)
    {
        var key = string.IsNullOrWhiteSpace(instanceKey) ? BuildCurrentInstanceScope() : instanceKey.Trim();
        _mutex = new Mutex(initiallyOwned: true, name: $@"Local\SessionGuard.App.{key}.Primary", createdNew: out var createdNew);
        _activateEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            name: $@"Local\SessionGuard.App.{key}.Activate");
        IsPrimaryInstance = createdNew;
    }

    public bool IsPrimaryInstance { get; }

    public void StartListening(Action onActivate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsPrimaryInstance)
        {
            throw new InvalidOperationException("Only the primary SessionGuard app instance can listen for activation requests.");
        }

        if (_listenerTask is not null)
        {
            return;
        }

        _listenerTask = Task.Run(() => ListenForActivation(onActivate, _cancellation.Token));
    }

    public void SignalActivation()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _activateEvent.Set();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        _activateEvent.Set();

        try
        {
            _listenerTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Ignore listener shutdown failures during app exit.
        }
        catch (ObjectDisposedException)
        {
            // Ignore disposal races during shutdown.
        }

        if (IsPrimaryInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Ignore if ownership was already released during shutdown.
            }
        }

        _cancellation.Dispose();
        _activateEvent.Dispose();
        _mutex.Dispose();
    }

    private void ListenForActivation(Action onActivate, CancellationToken cancellationToken)
    {
        var waitHandles = new WaitHandle[]
        {
            _activateEvent,
            cancellationToken.WaitHandle
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            var signaledIndex = WaitHandle.WaitAny(waitHandles);
            if (signaledIndex == 1 || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            onActivate();
        }
    }

    internal static string BuildCurrentInstanceScope()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(sid))
        {
            sid = Environment.UserName;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            executablePath = AppContext.BaseDirectory;
        }

        return BuildInstanceScopeKey(
            sid,
            executablePath,
            IsCurrentProcessElevated());
    }

    internal static string BuildInstanceScopeKey(string? sid, string? executablePath, bool isElevated)
    {
        var identity = string.IsNullOrWhiteSpace(sid) ? Environment.UserName : sid.Trim();
        var pathIdentity = NormalizeExecutablePath(executablePath);
        var scopeDescriptor = $"{identity}|{(isElevated ? "elevated" : "standard")}|{pathIdentity}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(scopeDescriptor));
        return Convert.ToHexString(hash);
    }

    internal static bool IsCurrentProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string NormalizeExecutablePath(string? executablePath)
    {
        var pathValue = string.IsNullOrWhiteSpace(executablePath) ? AppContext.BaseDirectory : executablePath.Trim();

        try
        {
            pathValue = Path.GetFullPath(pathValue);
        }
        catch (ArgumentException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (PathTooLongException)
        {
        }

        pathValue = pathValue.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return pathValue.ToUpperInvariant();
    }
}
