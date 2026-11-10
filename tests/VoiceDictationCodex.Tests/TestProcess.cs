using System.Diagnostics;
using VoiceDictationCodex.Services;

namespace VoiceDictationCodex.Tests;

/// <summary>Builds a small stand-in child process so runtime tests do not need a whisper binary.</summary>
internal static class TestProcess
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public static ProcessStartInfo Shell(string windowsPowerShell, string posixShell)
        => OperatingSystem.IsWindows()
            ? WhisperRuntime.CreateStartInfo("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-Command", windowsPowerShell })
            : WhisperRuntime.CreateStartInfo("/bin/sh", new[] { "-c", posixShell });

    public static WhisperRuntime Runtime(string windowsPowerShell, string posixShell)
        => new(_ => Shell(windowsPowerShell, posixShell));
}
