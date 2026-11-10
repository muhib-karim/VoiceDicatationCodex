using System;
using System.IO;

namespace VoiceDictationCodex.Services;

/// <summary>Locations of the files the app reads and writes.</summary>
public static class AppPaths
{
    public const string AppFolderName = "VoiceDictationCodex";

    /// <summary>Model files live under local app data so that a synced Documents folder does not upload them.</summary>
    public static string ModelsFolder
        => BuildModelsFolder(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static string MetadataFolder
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

    public static string ExportFolder
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppFolderName);

    public static string LogFolder
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName, "logs");

    internal static string BuildModelsFolder(string localAppData)
    {
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("The local application data folder is not available.");
        }

        return Path.Combine(localAppData, AppFolderName, "Models");
    }
}
