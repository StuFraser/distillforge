using DistillForge.Core.Contracts;

namespace DistillForge.Core.Tests;

public class ActionItemEditsTests
{
    private static UpdateActionItemRequest Req(string title = "Do it", string? summary = null, string? owner = null, string? due = null, string? colour = null) =>
        new(title, summary, owner, due, colour);

    [Fact]
    public void AcceptsMinimalAndFullRequests()
    {
        Assert.Null(ActionItemValidation.Validate(Req()));
        Assert.Null(ActionItemValidation.Validate(Req("T", "S", "Sam", "Friday", "lavender")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsBlankTitle(string title) => Assert.NotNull(ActionItemValidation.Validate(Req(title)));

    [Fact]
    public void RejectsOverlongFields()
    {
        Assert.NotNull(ActionItemValidation.Validate(Req(new string('x', ActionItemValidation.MaxTitle + 1))));
        Assert.NotNull(ActionItemValidation.Validate(Req(summary: new string('x', ActionItemValidation.MaxSummary + 1))));
        Assert.NotNull(ActionItemValidation.Validate(Req(owner: new string('x', ActionItemValidation.MaxOwner + 1))));
        Assert.NotNull(ActionItemValidation.Validate(Req(due: new string('x', ActionItemValidation.MaxDue + 1))));
    }

    [Fact]
    public void RejectsUnknownColourButAllowsEveryPaletteKey()
    {
        Assert.NotNull(ActionItemValidation.Validate(Req(colour: "#ff0000")));
        Assert.All(NoteColours.Keys, k => Assert.Null(ActionItemValidation.Validate(Req(colour: k))));
    }

    [Fact]
    public void PaletteHasTheSixAgreedColoursAndAutoDefaultsFollowDeadline()
    {
        Assert.Equal(["pink", "yellow", "lavender", "orange", "green", "blue"], NoteColours.Keys);
        Assert.Equal("yellow", NoteColours.DefaultFor(true));
        Assert.Equal("pink", NoteColours.DefaultFor(false));
    }
}
