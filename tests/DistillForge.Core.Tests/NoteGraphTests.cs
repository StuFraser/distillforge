using DistillForge.Core.Contracts;

namespace DistillForge.Core.Tests;

public class NoteGraphTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid();

    private static IReadOnlyDictionary<Guid, Guid?> Parents(params (Guid Id, Guid? Parent)[] n) =>
        n.ToDictionary(x => x.Id, x => x.Parent);

    [Fact]
    public void RootOfTopLevelIsItself_ChildResolvesToParent()
    {
        var p = Parents((A, null), (B, A));
        Assert.Equal(A, NoteGraph.Root(A, p));
        Assert.Equal(A, NoteGraph.Root(B, p));
    }

    [Fact]
    public void FlagsAreSymmetricAndOnlyForTopLevelNotes()
    {
        var notes = new[] { new NoteNode(A, null), new NoteNode(B, null), new NoteNode(C, A) };
        var result = NoteGraph.PossibleDuplicates(notes, [new DuplicateLink(A, B, false)]);
        Assert.Equal([B], result[A]);
        Assert.Equal([A], result[B]);
        Assert.False(result.ContainsKey(C)); // merged notes are hidden
    }

    [Fact]
    public void DismissedLinksAreIgnored()
    {
        var notes = new[] { new NoteNode(A, null), new NoteNode(B, null) };
        var result = NoteGraph.PossibleDuplicates(notes, [new DuplicateLink(A, B, true)]);
        Assert.Empty(result[A]);
    }

    [Fact]
    public void FlagOnMergedChildCountsForItsParent()
    {
        // C was merged into A; C was flagged against B, so A should now be flagged against B.
        var notes = new[] { new NoteNode(A, null), new NoteNode(B, null), new NoteNode(C, A) };
        var result = NoteGraph.PossibleDuplicates(notes, [new DuplicateLink(C, B, false)]);
        Assert.Equal([B], result[A]);
        Assert.Equal([A], result[B]);
    }

    [Fact]
    public void LinkBetweenNotesNowInTheSameGroupIsResolved()
    {
        var notes = new[] { new NoteNode(A, null), new NoteNode(B, A) };
        Assert.Empty(NoteGraph.PossibleDuplicates(notes, [new DuplicateLink(A, B, false)])[A]);
    }

    [Fact]
    public void LinksToUnknownNotesAreIgnored()
    {
        var notes = new[] { new NoteNode(A, null) };
        Assert.Empty(NoteGraph.PossibleDuplicates(notes, [new DuplicateLink(A, D, false)])[A]);
    }

    [Fact]
    public void MergeRules()
    {
        var p = Parents((A, null), (B, null), (C, A));
        Assert.Null(NoteGraph.ValidateMerge(B, A, p));
        Assert.Null(NoteGraph.ValidateMerge(A, B, p));            // parent with children can move under another note
        Assert.NotNull(NoteGraph.ValidateMerge(A, A, p));          // self
        Assert.NotNull(NoteGraph.ValidateMerge(A, C, p));          // into its own child
        Assert.NotNull(NoteGraph.ValidateMerge(B, D, p));          // unknown note
        Assert.Null(NoteGraph.ValidateMerge(B, C, p));             // target is a child: caller resolves to root A
    }
}
