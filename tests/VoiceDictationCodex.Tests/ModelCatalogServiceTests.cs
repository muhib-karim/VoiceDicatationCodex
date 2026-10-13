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
}
