using DistillForge.Core.Segmentation;
using DistillForge.Core.Text;

namespace DistillForge.Core.Tests;

public class SegmenterTests
{
    private static void AssertInvariants(string source, IReadOnlyList<Segment> segments, int max = 512)
    {
        int prevEnd = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            Assert.Equal($"S{i + 1}", s.Label);
            Assert.Equal(source[s.Start..s.End], s.Text);          // offsets round-trip
            Assert.True(s.Length <= max, $"{s.Label} is {s.Length} chars");
            Assert.True(s.Start >= prevEnd, "segments overlap or are out of order");
            Assert.False(char.IsWhiteSpace(s.Text[0]) || char.IsWhiteSpace(s.Text[^1]), "segment not trimmed");
            prevEnd = s.End;
        }
    }

    [Fact]
    public void Prose_SplitsOnBlankLines()
    {
        var src = "First paragraph has enough text to stand on its own here.\n\nSecond paragraph also has plenty of text in it.\n\n\nThird one is here and long enough too.";
        var segs = new Segmenter().Split(src, SegmentationStrategyKind.Prose);

        Assert.Equal(3, segs.Count);
        Assert.StartsWith("First", segs[0].Text);
        Assert.StartsWith("Second", segs[1].Text);
        Assert.StartsWith("Third", segs[2].Text);
        AssertInvariants(src, segs);
    }

    [Fact]
    public void Prose_MergesTinyHeadingIntoNextParagraph()
    {
        var src = "# Actions\n\nSam will send the revised budget to finance by Friday afternoon.";
        var segs = new Segmenter().Split(src, SegmentationStrategyKind.Prose);

        Assert.Single(segs);
        Assert.Equal(src, segs[0].Text);
    }

    [Fact]
    public void Transcript_OneSegmentPerTurn_NoMerging()
    {
        var src = "Sam: Morning.\nAlex: Hi.\nSam: I'll get the revised budget over by Friday.\n\nAlex: Great.";
        var segs = new Segmenter().Split(src, SegmentationStrategyKind.Transcript);

        Assert.Equal(4, segs.Count);
        Assert.Equal("Sam: I'll get the revised budget over by Friday.", segs[2].Text);
        AssertInvariants(src, segs);
    }

    [Fact]
    public void LongParagraph_SplitsAtSentenceBoundaries()
    {
        var sentence = "This is a reasonably long sentence about project delivery timelines and owners. ";
        var src = string.Concat(Enumerable.Repeat(sentence, 20)).TrimEnd();
        var segs = new Segmenter().Split(src, SegmentationStrategyKind.Prose);

        Assert.True(segs.Count > 1);
        Assert.All(segs, s => Assert.EndsWith(".", s.Text));
        AssertInvariants(src, segs);
    }

    [Fact]
    public void UnpunctuatedText_HardSplitsAtWhitespace_NeverMidWord()
    {
        var src = string.Join(' ', Enumerable.Range(0, 400).Select(i => $"word{i}"));
        var segs = new Segmenter().Split(src, SegmentationStrategyKind.Prose);

        Assert.True(segs.Count > 1);
        AssertInvariants(src, segs);
        var words = src.Split(' ').ToHashSet();
        Assert.All(segs, s => Assert.All(s.Text.Split(' '), w => Assert.Contains(w, words)));
    }

    [Fact]
    public void SingleGiantToken_IsCutAtLimit()
    {
        var src = new string('x', 1300);
        var segs = new Segmenter().Split(src, SegmentationStrategyKind.Prose);

        Assert.Equal(3, segs.Count);
        AssertInvariants(src, segs);
    }

    [Fact]
    public void OffsetsAreAgainstNormalisedSource()
    {
        var raw = "Line one of the first paragraph is here.\r\n\r\nSecond paragraph follows with more words.";
        var src = SourceNormaliser.Normalise(raw);
        var segs = new Segmenter().Split(src, SegmentationStrategyKind.Prose);

        Assert.DoesNotContain('\r', src);
        Assert.Equal(2, segs.Count);
        AssertInvariants(src, segs);
    }

    [Fact]
    public void EmptyOrWhitespace_ProducesNoSegments()
    {
        Assert.Empty(new Segmenter().Split("  \n\n \n", SegmentationStrategyKind.Prose));
        Assert.Empty(new Segmenter().Split("", SegmentationStrategyKind.Transcript));
    }

    [Fact]
    public void StrategyDetector_RecognisesTranscripts()
    {
        var transcript = "[00:01] Sam: Morning all.\n[00:05] Alex: Hi.\n[00:09] Sam: Budget first.\nPriya: Agreed.";
        var prose = "The project will deliver a new reporting module.\n\nIt needs sign-off from finance: they meet monthly.";

        Assert.Equal(SegmentationStrategyKind.Transcript, StrategyDetector.Detect(transcript));
        Assert.Equal(SegmentationStrategyKind.Prose, StrategyDetector.Detect(prose));
    }
}
