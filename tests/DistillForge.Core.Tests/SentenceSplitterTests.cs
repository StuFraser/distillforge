using DistillForge.Core.Segmentation;

namespace DistillForge.Core.Tests;

public class SentenceSplitterTests
{
    private static List<string> Split(string s) =>
        SentenceSplitter.Split(s, 0, s.Length).Select(r => s[r.Start..r.End]).ToList();

    [Fact]
    public void SplitsOnTerminators()
    {
        Assert.Equal(["One here.", "Two here?", "Three!"], Split("One here. Two here? Three!"));
    }

    [Theory]
    [InlineData("Acme Inc. Signed the deal.")]
    [InlineData("Meet Dr. Smith tomorrow.")]
    [InlineData("Ask J. Smith about it.")]
    [InlineData("See clause 4.2 for details.")]
    [InlineData("Bring items, e.g. the laptop and charger.")]
    [InlineData("Visit example.com today.")]
    public void DoesNotSplitAbbreviationsInitialsOrNumbers(string text)
    {
        Assert.Single(Split(text));
    }

    [Fact]
    public void KeepsClosingQuoteWithSentence()
    {
        Assert.Equal(["He said \"done.\"", "Next item."], Split("He said \"done.\" Next item."));
    }
}
