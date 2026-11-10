namespace VoiceDictationCodex.Models;

/// <param name="DownloadSize">Approximate download size in megabytes, for display.</param>
/// <param name="SizeBytes">Exact size of the model file, or 0 when unknown.</param>
/// <param name="Sha256">Expected SHA-256 of the model file as hex, or null when unknown.</param>
public record WhisperModelInfo(
    string Id,
    string Name,
    string Description,
    double DownloadSize,
    string LocalPath,
    string LanguageSupport,
    bool IsInstalled,
    long SizeBytes = 0,
    string? Sha256 = null);
