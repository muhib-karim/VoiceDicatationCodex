namespace VoiceDictationCodex.Models;

public class TranscriptionState
{
    public string Text { get; set; } = string.Empty;
    public string Duration { get; set; } = "00:00";
    public int WordCount { get; set; }
    public string Language { get; set; } = "Auto";
    public string ModelName { get; set; } = "";

    public static int CountWords(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
