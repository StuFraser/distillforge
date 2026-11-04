namespace DistillForge.Core.Text;

/// <summary>
/// Normalises source text once at ingest, before any offsets are computed.
/// After this the canonical source text is immutable: every segment and evidence
/// offset is stored against the string this method returns.
/// </summary>
public static class SourceNormaliser
{
    public static string Normalise(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        // \r\n -> \n first, then any stray \r -> \n.
        return text.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('﻿');
    }
}
