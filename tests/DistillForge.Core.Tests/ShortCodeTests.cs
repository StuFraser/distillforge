using DistillForge.Core.Codes;

namespace DistillForge.Core.Tests;

public class ShortCodeTests
{
    [Fact]
    public void New_is_six_characters_from_the_alphabet()
    {
        for (var i = 0; i < 200; i++)
        {
            var c = ShortCode.New();
            Assert.Equal(ShortCode.Length, c.Length);
            Assert.All(c, ch => Assert.Contains(ch, ShortCode.Alphabet));
        }
    }

    [Theory]
    [InlineData("W-4K7Q2M", ShortCodeKind.Workspace, "4K7Q2M")]
    [InlineData("n-9f2m8x", ShortCodeKind.Note, "9F2M8X")]
    [InlineData(" W 4K7Q2M ", ShortCodeKind.Workspace, "4K7Q2M")]
    [InlineData("WABCDEF", ShortCodeKind.Workspace, "ABCDEF")]
    [InlineData("N-OILO1O", ShortCodeKind.Note, "011010")]
    public void TryParse_accepts_what_people_type(string input, ShortCodeKind kind, string code)
    {
        Assert.True(ShortCode.TryParse(input, out var k, out var c));
        Assert.Equal(kind, k);
        Assert.Equal(code, c);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("X-4K7Q2M")]
    [InlineData("W-4K7Q2")]
    [InlineData("W-4K7Q2MM")]
    [InlineData("W-4K7Q2U")]
    [InlineData("4K7Q2M")]
    public void TryParse_rejects_everything_else(string? input) => Assert.False(ShortCode.TryParse(input, out _, out _));

    [Fact]
    public void Round_trips_the_display_form()
    {
        var code = ShortCode.New();
        Assert.True(ShortCode.TryParse(ShortCode.ForNote(code), out var kind, out var parsed));
        Assert.Equal(ShortCodeKind.Note, kind);
        Assert.Equal(code, parsed);
    }
}
