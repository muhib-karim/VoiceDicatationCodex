using System.Collections.Generic;
using VoiceDictationCodex.Models;

namespace VoiceDictationCodex.Services;

public class ModelCatalogService
{
    /// <summary>
    /// Sizes and SHA-256 values match the files at revision <see cref="ModelDownloadService.ModelRevision"/>
    /// of the ggerganov/whisper.cpp repository on Hugging Face.
    /// </summary>
    public IReadOnlyList<WhisperModelInfo> GetBuiltInCatalog()
    {
        return new List<WhisperModelInfo>
        {
            new(
                Id: "ggml-base.en",
                Name: "Whisper Base (English)",
                Description: "Fast English-only model for live captioning",
                DownloadSize: 148,
                LocalPath: string.Empty,
                LanguageSupport: "English",
                IsInstalled: false,
                SizeBytes: 147964211,
                Sha256: "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002"),
            new(
                Id: "ggml-small",
                Name: "Whisper Small Multilingual",
                Description: "Balanced accuracy across 50+ languages",
                DownloadSize: 488,
                LocalPath: string.Empty,
                LanguageSupport: "Multi",
                IsInstalled: false,
                SizeBytes: 487601967,
                Sha256: "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b"),
            new(
                Id: "ggml-medium",
                Name: "Whisper Medium Multilingual",
                Description: "High accuracy for production workloads",
                DownloadSize: 1534,
                LocalPath: string.Empty,
                LanguageSupport: "Multi",
                IsInstalled: false,
                SizeBytes: 1533763059,
                Sha256: "6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208"),
        };
    }
}
