using System;
using System.IO;

namespace VoiceDictationCodex.Services;

/// <summary>Appends unhandled exceptions to a local log file. Never throws.</summary>
internal static class CrashLog
{
    public static void Write(Exception exception) => Write(exception, AppPaths.LogFolder);

    internal static void Write(Exception exception, string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "errors.log"), $"{DateTime.UtcNow:O} {exception}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Logging is best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Logging is best effort.
        }
    }
}
