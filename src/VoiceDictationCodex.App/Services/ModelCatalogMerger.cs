using System;
using System.Collections.Generic;
using System.Linq;
using VoiceDictationCodex.Models;

namespace VoiceDictationCodex.Services;

public static class ModelCatalogMerger
{
    /// <summary>
    /// Combines the built-in catalog with saved metadata. A model counts as installed only when the
    /// metadata says so and its file still exists.
    /// </summary>
    public static IReadOnlyList<WhisperModelInfo> Merge(
        IReadOnlyList<WhisperModelInfo> catalog,
        IReadOnlyList<WhisperModelInfo> saved,
        Func<string, bool> fileExists)
    {
        var merged = new List<WhisperModelInfo>();
        foreach (var model in catalog)
        {
            var match = saved.FirstOrDefault(candidate => string.Equals(candidate?.Id, model.Id, StringComparison.OrdinalIgnoreCase));
            var installed = match is not null
                && match.IsInstalled
                && !string.IsNullOrWhiteSpace(match.LocalPath)
                && fileExists(match.LocalPath);

            merged.Add(installed
                ? model with { LocalPath = match!.LocalPath, IsInstalled = true }
                : model with { LocalPath = string.Empty, IsInstalled = false });
        }

        return merged;
    }
}
