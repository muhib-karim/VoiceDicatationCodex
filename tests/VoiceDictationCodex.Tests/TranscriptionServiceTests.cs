using VoiceDictationCodex.Services;
using Xunit;

namespace VoiceDictationCodex.Tests;

public class TranscriptionServiceTests
{
    private static (TranscriptionService Service, List<string> Updates) Create()
    {
        var service = new TranscriptionService(new WhisperRuntime());
        var updates = new List<string>();
        service.TextUpdated += (_, text) => updates.Add(text);
        return (service, updates);
    }

    [Fact]
    public void JsonLines_AppendTextProperty()
    {
        var (service, updates) = Create();

        service.ProcessLine("{\"text\":\"Hello\"}");
        service.ProcessLine("{\"text\":\" world\"}");

        Assert.Equal(new[] { "Hello", "Hello world" }, updates);
    }

    [Fact]
    public void JsonWithoutTextOrBlankText_IsIgnored()
    {
        var (service, updates) = Create();

        service.ProcessLine("{\"progress\":42}");
        service.ProcessLine("{\"text\":\"   \"}");

        Assert.Empty(updates);
    }

    [Fact]
    public void NonJsonLines_FallBackToRawText()
    {
        var (service, updates) = Create();

        service.ProcessLine("plain output");
        service.ProcessLine("   ");

        var update = Assert.Single(updates);
        Assert.Equal("plain output" + Environment.NewLine, update);
    }
}
