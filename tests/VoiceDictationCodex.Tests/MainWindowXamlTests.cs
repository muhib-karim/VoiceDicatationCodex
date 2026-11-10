using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using VoiceDictationCodex.ViewModels;
using Xunit;

namespace VoiceDictationCodex.Tests;

/// <summary>Structural checks on the window markup. They parse it as XML and need no UI thread.</summary>
public class MainWindowXamlTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static string XamlPath => Path.Combine(AppContext.BaseDirectory, "Xaml", "MainWindow.xaml");

    [Fact]
    public void ScrollViewers_AreNotDirectChildrenOfAStackPanel()
    {
        var document = XDocument.Load(XamlPath);
        var scrollViewers = document.Descendants(Presentation + "ScrollViewer").ToList();
        var unbounded = scrollViewers.Where(element => element.Parent?.Name == Presentation + "StackPanel").ToList();

        // A StackPanel gives its children unlimited height, so a ScrollViewer inside one never scrolls.
        Assert.NotEmpty(scrollViewers);
        Assert.Empty(unbounded);
    }

    [Fact]
    public void EveryCommandBinding_ExistsOnTheViewModel()
    {
        var names = Regex.Matches(File.ReadAllText(XamlPath), @"\{Binding (\w+Command)\}")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.Contains("ToggleRecordingCommand", names);
        Assert.Contains("CancelDownloadCommand", names);
        Assert.All(names, name => Assert.NotNull(typeof(MainViewModel).GetProperty(name)));
    }

    [Theory]
    [InlineData("AvailableModels")]
    [InlineData("SelectedModel")]
    [InlineData("RecordingButtonText")]
    [InlineData("RecordingStatusText")]
    [InlineData("RecordingIndicatorBrush")]
    [InlineData("ActiveTranscription")]
    public void ViewModelProperties_UsedByTheWindow_ArePublic(string property)
    {
        Assert.Contains("{Binding " + property, File.ReadAllText(XamlPath));
        Assert.NotNull(typeof(MainViewModel).GetProperty(property));
    }
}
