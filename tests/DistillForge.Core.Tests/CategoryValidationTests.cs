using DistillForge.Core.Contracts;

namespace DistillForge.Core.Tests;

public class CategoryValidationTests
{
    [Fact]
    public void AcceptsNameWithOrWithoutColour()
    {
        Assert.Null(CategoryValidation.Validate(new("Features", null)));
        Assert.Null(CategoryValidation.Validate(new("Bugs", "green")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsBlankName(string name) => Assert.NotNull(CategoryValidation.Validate(new(name, null)));

    [Fact]
    public void RejectsOverlongNameAndUnknownColour()
    {
        Assert.NotNull(CategoryValidation.Validate(new(new string('x', CategoryValidation.MaxName + 1), null)));
        Assert.NotNull(CategoryValidation.Validate(new("Ok", "chartreuse")));
    }

    [Fact]
    public void NameClashIsCaseAndWhitespaceInsensitive()
    {
        Assert.True(CategoryValidation.NameTaken(" features ", ["Bugs", "Features"]));
        Assert.False(CategoryValidation.NameTaken("Reviews", ["Bugs", "Features"]));
    }
}
