using VoiceDictationCodex.Models;
using Xunit;

namespace VoiceDictationCodex.Tests;

public class TranscriptionStateTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("hello", 1)]
    [InlineData("hello  world", 2)]
    [InlineData("line one\r\nline two\tend", 5)]
    public void CountWords_SplitsOnAnyWhitespace(string? text, int expected)
    {
        Assert.Equal(expected, TranscriptionState.CountWords(text));
    }

    [Fact]
    public void Defaults_AreEmptySession()
    {
        var state = new TranscriptionState();

        Assert.Equal(string.Empty, state.Text);
        Assert.Equal("00:00", state.Duration);
        Assert.Equal(0, state.WordCount);
        Assert.Equal("Auto", state.Language);
    }
}
