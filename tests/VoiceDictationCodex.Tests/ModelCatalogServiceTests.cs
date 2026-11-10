using VoiceDictationCodex.Services;
using Xunit;

namespace VoiceDictationCodex.Tests;

public class ModelCatalogServiceTests
{
    [Fact]
    public void BuiltInCatalog_ContainsThreeUninstalledModelsWithUniqueIds()
    {
        var catalog = new ModelCatalogService().GetBuiltInCatalog();

        Assert.Equal(3, catalog.Count);
        Assert.Equal(catalog.Count, catalog.Select(m => m.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(catalog, model =>
        {
            Assert.False(model.IsInstalled);
            Assert.Equal(string.Empty, model.LocalPath);
            Assert.StartsWith("ggml-", model.Id);
            Assert.True(model.DownloadSize > 0);
        });
    }

    [Theory]
    [InlineData("ggml-base.en", 147964211L, "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002")]
    [InlineData("ggml-small", 487601967L, "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b")]
    [InlineData("ggml-medium", 1533763059L, "6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208")]
    public void BuiltInCatalog_PinsExactSizeAndSha256(string id, long sizeBytes, string sha256)
    {
        var model = new ModelCatalogService().GetBuiltInCatalog().Single(m => m.Id == id);

        Assert.Equal(sizeBytes, model.SizeBytes);
        Assert.Equal(sha256, model.Sha256);
        Assert.Equal(Math.Round(sizeBytes / 1_000_000.0), model.DownloadSize);
    }

    [Fact]
    public void BuiltInCatalog_EveryModelHasAWellFormedSha256AndValidId()
    {
        var catalog = new ModelCatalogService().GetBuiltInCatalog();

        Assert.All(catalog, model =>
        {
            Assert.NotNull(model.Sha256);
            Assert.Matches("^[0-9a-f]{64}$", model.Sha256);
            Assert.True(model.SizeBytes > 0);
            ModelDownloadService.ValidateModelId(model.Id);
        });
    }
}
