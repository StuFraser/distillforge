namespace DistillForge.Core.Text;

/// <summary>
/// Cheap pre-call token estimate (~4 chars per token). Used for sizing windows;
/// exact counts come back in the provider's usage block after the call.
/// </summary>
public static class TokenEstimator
{
    public const double CharsPerToken = 4.0;

    public static int Estimate(string text) => Estimate(text.Length);

    public static int Estimate(int charCount) => (int)Math.Ceiling(charCount / CharsPerToken);
}
