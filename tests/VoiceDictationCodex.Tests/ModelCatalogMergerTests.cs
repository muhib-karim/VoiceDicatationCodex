using VoiceDictationCodex.Models;
using VoiceDictationCodex.Services;
using Xunit;

namespace VoiceDictationCodex.Tests;

public class ModelCatalogMergerTests
{
    private static readonly IReadOnlyList<WhisperModelInfo> Catalog = new ModelCatalogService().GetBuiltInCatalog();

    private static WhisperModelInfo Saved(string id, bool installed = true, string path = "model.bin")
        => new(id, "Stale name", "Stale description", 1, path, "English", installed);

    [Fact]
    public void ModelsWithoutSavedMetadata_AreNotInstalled()
    {
        var merged = ModelCatalogMerger.Merge(Catalog, Array.Empty<WhisperModelInfo>(), _ => true);

        Assert.Equal(Catalog, merged);
    }

    [Fact]
    public void SavedModelWhoseFileExists_IsInstalled_AndKeepsCatalogDetails()
    {
        var merged = ModelCatalogMerger.Merge(Catalog, new[] { Saved("GGML-SMALL") }, path => path == "model.bin");

        var model = merged.Single(m => m.Id == "ggml-small");
        Assert.True(model.IsInstalled);
        Assert.Equal("model.bin", model.LocalPath);
        Assert.Equal("Whisper Small Multilingual", model.Name);
        Assert.NotNull(model.Sha256);
    }

    [Fact]
    public void SavedModelWhoseFileIsMissing_IsNotInstalled()
    {
        var merged = ModelCatalogMerger.Merge(Catalog, new[] { Saved("ggml-small") }, _ => false);

        var model = merged.Single(m => m.Id == "ggml-small");
        Assert.False(model.IsInstalled);
        Assert.Equal(string.Empty, model.LocalPath);
    }

    [Theory]
    [InlineData(false, "model.bin")]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    public void SavedModelThatIsNotInstalledOrHasNoPath_IsNotInstalled(bool installed, string path)
    {
        var merged = ModelCatalogMerger.Merge(Catalog, new[] { Saved("ggml-small", installed, path) }, _ => true);

        Assert.False(merged.Single(m => m.Id == "ggml-small").IsInstalled);
    }
}
