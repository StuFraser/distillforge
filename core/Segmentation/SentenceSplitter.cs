namespace DistillForge.Core.Segmentation;

/// <summary>
/// Pragmatic rule-based sentence splitter (.NET has no Intl.Segmenter equivalent).
/// A boundary is a terminator (. ? !), optionally followed by closing quotes/brackets,
/// then whitespace, then a character that is not lower case. Abbreviations
/// ("Inc.", "e.g."), initials ("J. Smith") and numbers ("4.2") are not boundaries.
/// </summary>
public static class SentenceSplitter
{
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "dr", "prof", "sr", "jr", "st", "rev", "hon",
        "inc", "ltd", "co", "corp", "plc", "llc", "pty", "dept", "est", "approx",
        "vs", "etc", "e.g", "i.e", "eg", "ie", "cf", "al", "fig", "no", "nos", "vol", "pp", "para", "cl",
        "jan", "feb", "mar", "apr", "jun", "jul", "aug", "sep", "sept", "oct", "nov", "dec",
        "mon", "tue", "tues", "wed", "thu", "thur", "thurs", "fri", "sat", "sun",
    };

    /// <summary>Returns sentence ranges within [start, end), trimmed of surrounding whitespace.</summary>
    public static IReadOnlyList<(int Start, int End)> Split(string text, int start, int end)
    {
        var result = new List<(int Start, int End)>();
        while (start < end && char.IsWhiteSpace(text[start])) start++;
        int sentenceStart = start;

        for (int i = start; i < end; i++)
        {
            char c = text[i];
            if (c is not ('.' or '?' or '!')) continue;

            int j = i + 1;
            while (j < end && IsTerminatorTail(text[j])) j++;
            if (j >= end) break;
            if (!char.IsWhiteSpace(text[j])) continue;          // "4.2", "e.g.x", "example.com"
            if (c == '.' && IsAbbreviationOrInitial(text, sentenceStart, i)) continue;

            int k = j;
            while (k < end && char.IsWhiteSpace(text[k])) k++;
            if (k >= end) break;
            if (char.IsLower(text[k])) continue;                // "e.g. the", "approx. ten"

            result.Add((sentenceStart, j));
            sentenceStart = k;
            i = k - 1;
        }

        int last = end;
        while (last > sentenceStart && char.IsWhiteSpace(text[last - 1])) last--;
        if (last > sentenceStart) result.Add((sentenceStart, last));
        return result;
    }

    private static bool IsTerminatorTail(char c) =>
        c is '.' or '?' or '!' or '"' or '\'' or ')' or ']' or '”' or '’';

    private static bool IsAbbreviationOrInitial(string text, int sentenceStart, int dotIndex)
    {
        int ws = dotIndex;
        while (ws > sentenceStart && (char.IsLetter(text[ws - 1]) || text[ws - 1] == '.')) ws--;
        if (ws == dotIndex) return false;

        var word = text.AsSpan(ws, dotIndex - ws);
        if (word.Length == 1 && char.IsUpper(word[0])) return true;       // initial: "J. Smith"
        return Abbreviations.Contains(word.Trim('.').ToString());
    }
}
