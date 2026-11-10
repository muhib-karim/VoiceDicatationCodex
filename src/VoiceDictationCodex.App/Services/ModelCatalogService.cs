using System.Collections.Generic;
using VoiceDictationCodex.Models;

namespace VoiceDictationCodex.Services;

public class ModelCatalogService
{
    public IReadOnlyList<WhisperModelInfo> GetBuiltInCatalog()
    {
        return new List<WhisperModelInfo>
        {
            new(
                Id: "ggml-base.en",
                Name: "Whisper Base (English)",
                Description: "Fast English-only model for live captioning",
                DownloadSize: 140,
                LocalPath: string.Empty,
                LanguageSupport: "English",
                IsInstalled: false),
            new(
                Id: "ggml-small",
                Name: "Whisper Small Multilingual",
                Description: "Balanced accuracy across 50+ languages",
                DownloadSize: 480,
                LocalPath: string.Empty,
                LanguageSupport: "Multi",
                IsInstalled: false),
            new(
                Id: "ggml-medium",
                Name: "Whisper Medium Multilingual",
                Description: "High accuracy for production workloads",
                DownloadSize: 1500,
                LocalPath: string.Empty,
                LanguageSupport: "Multi",
                IsInstalled: false),
        };
    }
}
