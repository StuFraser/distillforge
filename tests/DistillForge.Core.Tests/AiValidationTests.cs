using DistillForge.Core.Contracts;

namespace DistillForge.Core.Tests;

public class AiValidationTests
{
    [Theory]
    [InlineData("http://host:11434/v1", true)]
    [InlineData("https://api.openai.com/v1/", true)]
    [InlineData("https://openrouter.ai/api/v1", true)]
    [InlineData("http://host:11434", false)]
    [InlineData("http://host:11434/", false)]
    [InlineData("host:11434/v1", false)]
    [InlineData("ftp://host/v1", false)]
    [InlineData("http://host/v1?x=1", false)]
    [InlineData("", false)]
    public void BaseUrlRules(string url, bool ok) => Assert.Equal(ok, AiValidation.BaseUrlError(url) is null);

    [Fact]
    public void NormaliseTrimsWhitespaceAndSlashes() =>
        Assert.Equal("http://h/v1", AiValidation.NormaliseBaseUrl("  http://h/v1//  "));

    [Fact]
    public void ProviderRules()
    {
        Assert.Null(AiValidation.ValidateProvider(new("Ollama", "http://h/v1", null)));
        Assert.NotNull(AiValidation.ValidateProvider(new(" ", "http://h/v1", null)));
        Assert.NotNull(AiValidation.ValidateProvider(new("x", "http://h/v1", null, 1)));
    }

    [Fact]
    public void ModelRules()
    {
        Assert.Null(AiValidation.ValidateModel(new("llama3.1:8b", null, 8192, 2048, ResponseFormatMode.JsonSchema, false)));
        Assert.NotNull(AiValidation.ValidateModel(new("", null, 8192, 2048, ResponseFormatMode.JsonSchema, false)));
        Assert.NotNull(AiValidation.ValidateModel(new("m", null, 100, 50, ResponseFormatMode.JsonSchema, false)));
        Assert.NotNull(AiValidation.ValidateModel(new("m", null, 8192, 2048, ResponseFormatMode.JsonSchema, false, (ThinkingMode)99)));
        Assert.Null(AiValidation.ValidateModel(new("m", null, 8192, 2048, ResponseFormatMode.JsonSchema, false, ThinkingMode.Off)));
        Assert.NotNull(AiValidation.ValidateModel(new("m", null, 8192, 8192, ResponseFormatMode.JsonSchema, false)));
        Assert.NotNull(AiValidation.ValidateModel(new("m", null, 8192, 2048, (ResponseFormatMode)99, false)));
    }

    [Fact]
    public void LabelPrefersDisplayName()
    {
        Assert.Equal("Llama · Home", AiValidation.ModelLabel("Home", "llama3.1:8b", "Llama"));
        Assert.Equal("llama3.1:8b · Home", AiValidation.ModelLabel("Home", "llama3.1:8b", null));
    }
}

public class ThinkingModeTests
{
    [Theory]
    [InlineData(ThinkingMode.Default, null)]
    [InlineData(ThinkingMode.Off, "none")]
    [InlineData(ThinkingMode.Low, "low")]
    [InlineData(ThinkingMode.Medium, "medium")]
    [InlineData(ThinkingMode.High, "high")]
    public void Maps_to_the_reasoning_effort_value(ThinkingMode mode, string? expected) => Assert.Equal(expected, mode.ToEffort());
}
