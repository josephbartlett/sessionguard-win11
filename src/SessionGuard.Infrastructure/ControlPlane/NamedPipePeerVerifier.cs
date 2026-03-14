using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SessionGuard.Infrastructure.ControlPlane;

internal static class NamedPipePeerVerifier
{
    public static void EnsureTrustedServerExecutable(SafePipeHandle pipeHandle)
    {
        var serverExecutable = GetServerExecutablePath(pipeHandle);
        if (string.IsNullOrWhiteSpace(serverExecutable))
        {
            throw new InvalidDataException("Connected to a SessionGuard pipe server, but could not resolve the server executable path.");
        }

        if (!IsTrustedServerExecutable(serverExecutable))
        {
            throw new InvalidDataException(
                $"Connected to an unexpected SessionGuard pipe server executable at '{serverExecutable}'.");
        }
    }

    internal static bool IsTrustedServerExecutable(string serverExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(serverExecutablePath))
        {
            return false;
        }

        var normalizedServerPath = NormalizePath(serverExecutablePath);
        if (!string.Equals(
                Path.GetFileName(normalizedServerPath),
                "SessionGuard.Service.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedSiblingExecutable = Path.Combine(AppContext.BaseDirectory, "SessionGuard.Service.exe");
        if (File.Exists(expectedSiblingExecutable))
        {
            return string.Equals(
                normalizedServerPath,
                NormalizePath(expectedSiblingExecutable),
                StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static string GetServerExecutablePath(SafePipeHandle pipeHandle)
    {
        if (!GetNamedPipeServerProcessId(pipeHandle, out var serverProcessId) || serverProcessId == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not resolve the SessionGuard pipe server process ID.");
        }

        using var processHandle = OpenProcess(ProcessAccessFlags.QueryLimitedInformation, false, serverProcessId);
        if (processHandle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the SessionGuard pipe server process.");
        }

        var capacity = 1024;
        var builder = new char[capacity];
        if (!QueryFullProcessImageName(processHandle, 0, builder, ref capacity))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not query the SessionGuard pipe server executable path.");
        }

        return new string(builder, 0, capacity);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    [Flags]
    private enum ProcessAccessFlags : uint
    {
        QueryLimitedInformation = 0x1000
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(
        SafePipeHandle pipeHandle,
        out uint serverProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        ProcessAccessFlags desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        SafeProcessHandle processHandle,
        uint flags,
        [Out] char[] executablePath,
        ref int size);
}
